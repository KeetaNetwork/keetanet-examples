package network.keeta.examples;

import java.math.BigInteger;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.ArrayList;
import java.util.Base64;
import java.util.List;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * Java network client that uses HTTP API calls for head/balance/vote/publish.
 * Vote staple construction stays in Rust via WASM.
 */
public class UserClient implements AutoCloseable {
    private static final Pattern VOTE_BINARY_PATTERN = Pattern.compile("\"\\$binary\"\\s*:\\s*\"([^\"]+)\"");
    private static final Pattern PUBLISH_PATTERN = Pattern.compile("\"publish\"\\s*:\\s*(true|false)");
    private static final Pattern HEAD_PATTERN = Pattern.compile("\"currentHeadBlock\"\\s*:\\s*(null|\"([0-9A-Fa-f]+)\")");
    private static final Pattern BALANCE_PATTERN = Pattern.compile("\"balance\"\\s*:\\s*\"([^\"]+)\"");
    private static final Pattern BLOCK_HASH_PATTERN = Pattern.compile("\"\\$hash\"\\s*:\\s*\"([0-9A-Fa-f]+)\"");

    private static final String[] TEST_REP_APIS = {
        "https://rep1.test.network.api.keeta.com/api",
        "https://rep2.test.network.api.keeta.com/api",
        "https://rep3.test.network.api.keeta.com/api",
        "https://rep4.test.network.api.keeta.com/api"
    };
    private static final long NETWORK_TEST = 0x54455354L;
    private static final long SUCCESSOR_RETRY_DELAY_MS = 5L * 60L * 1000L;
    private static final boolean DEBUG = "1".equals(System.getenv("KEETA_DEBUG"))
        || "true".equalsIgnoreCase(System.getenv("KEETA_DEBUG"));

    private final String networkName;
    private final long networkId;
    private final Account signer;
    private final String[] repApis;
    private final HttpClient httpClient;

    private static final class RepresentativeVote {
        private final String api;
        private final byte[] vote;

        private RepresentativeVote(String api, byte[] vote) {
            this.api = api;
            this.vote = vote;
        }
    }

    private static final class PendingBlockCandidate {
        private final String hash;
        private final byte[] bytes;

        private PendingBlockCandidate(String hash, byte[] bytes) {
            this.hash = hash;
            this.bytes = bytes;
        }
    }

    private static final class FeeRequirement {
        private final String amount;
        private final String payTo;
        private final String token;

        private FeeRequirement(String amount, String payTo, String token) {
            this.amount = amount;
            this.payTo = payTo;
            this.token = token;
        }
    }

    private UserClient(String networkName, long networkId, Account signer, String[] repApis) {
        this.networkName = networkName;
        this.networkId = networkId;
        this.signer = signer;
        this.repApis = repApis;
        this.httpClient = HttpClient.newBuilder()
            .connectTimeout(Duration.ofSeconds(10))
            .build();
    }

    public static UserClient fromNetwork(String networkName, Account signer) {
        if (!"test".equals(networkName)) {
            throw new IllegalArgumentException("Only test network is currently supported in this example");
        }
        return new UserClient(networkName, NETWORK_TEST, signer, TEST_REP_APIS);
    }

    public Account getBaseToken() {
        long ptr = KeetaNetJNI.networkBaseToken(networkId);
        if (ptr == 0) {
            throw new RuntimeException("Failed to derive base token");
        }
        return Account.fromNativePtr(ptr);
    }

    public BigInteger getBalance(Account account, Account token) {
        String path = "/node/ledger/account/" + urlEncode(account.publicKeyString())
            + "/balance/" + urlEncode(token.publicKeyString());
        String response = callGet(path);
        Matcher matcher = BALANCE_PATTERN.matcher(response);
        if (!matcher.find()) {
            throw new RuntimeException("Failed to parse balance response");
        }
        String raw = matcher.group(1);
        if (raw.startsWith("0x") || raw.startsWith("0X")) {
            return new BigInteger(raw.substring(2), 16);
        }
        return new BigInteger(raw);
    }

    public byte[] head() {
        if (signer == null) {
            throw new IllegalStateException("This UserClient has no default signer account");
        }
        return head(signer);
    }

    public byte[] head(Account account) {
        String path = "/node/ledger/account/" + urlEncode(account.publicKeyString());
        String response = callGet(path);
        Matcher matcher = HEAD_PATTERN.matcher(response);
        if (!matcher.find()) {
            throw new RuntimeException("Failed to parse account head response");
        }
        String hash = matcher.group(2);
        if (hash == null) {
            return null;
        }
        return hexToBytes(hash);
    }

    public boolean transmit(Block.SignedBlock... blocks) {
        RuntimeException lastError = null;
        for (int attempt = 0; attempt < 2; attempt++) {
            try {
                return transmitOnce(blocks);
            } catch (RuntimeException e) {
                lastError = e;
                debug("Transmit attempt " + (attempt + 1) + " failed: " + e.getMessage());
                if (attempt == 0 && (isSuccessorVoteExistsError(e) || isPublishFailedError(e))) {
                    boolean recovered = false;
                    for (Block.SignedBlock block : blocks) {
                        String account = block.getAccountPublicKeyString();
                        recovered = recoverAccount(account) || recovered;
                    }
                    if (!recovered) {
                        try {
                            Thread.sleep(SUCCESSOR_RETRY_DELAY_MS);
                        } catch (InterruptedException interrupted) {
                            Thread.currentThread().interrupt();
                            throw new RuntimeException("Interrupted while waiting to retry successor vote", interrupted);
                        }
                    }
                    continue;
                }
                throw e;
            }
        }
        throw lastError == null ? new RuntimeException("Transmit failed") : lastError;
    }

    private boolean transmitOnce(Block.SignedBlock... blocks) {
        List<Block.SignedBlock> allBlocks = new ArrayList<>();
        for (Block.SignedBlock block : blocks) {
            allBlocks.add(block);
        }
        Block.SignedBlock feeBlock = null;
        try {
            byte[][] blockBytes = toBlockBytes(allBlocks);
            List<RepresentativeVote> temporaryVotes = requestTemporaryVotes(blockBytes);
            if (temporaryVotes.isEmpty()) {
                throw new RuntimeException("No temporary votes were returned by representatives");
            }

            feeBlock = buildFeeBlockIfRequired(temporaryVotes, allBlocks);
            if (feeBlock != null) {
                allBlocks.add(feeBlock);
                blockBytes = toBlockBytes(allBlocks);
            }

            List<byte[]> permanentVotes = requestPermanentVotes(blockBytes, temporaryVotes);
            if (feeBlock != null && permanentVotes.isEmpty()) {
                List<RepresentativeVote> feeBlockTemporaryVotes = requestTemporaryVotes(blockBytes);
                if (!feeBlockTemporaryVotes.isEmpty()) {
                    permanentVotes = requestPermanentVotes(blockBytes, feeBlockTemporaryVotes);
                }
            }
            List<byte[]> votesForStaple;
            if (permanentVotes.isEmpty()) {
                if (feeBlock != null) {
                    throw new RuntimeException("Failed to obtain permanent votes for fee block");
                }
                votesForStaple = new ArrayList<>(temporaryVotes.size());
                for (RepresentativeVote vote : temporaryVotes) {
                    votesForStaple.add(vote.vote);
                }
            } else {
                votesForStaple = permanentVotes;
            }

            byte[][] voteBytes = votesForStaple.toArray(new byte[0][]);
            byte[] staple = KeetaNetJNI.createVoteStaple(blockBytes, voteBytes);
            if (staple == null || staple.length == 0) {
                throw new RuntimeException("Failed to build vote staple");
            }

            String stapleBase64 = Base64.getEncoder().encodeToString(staple);
            String body = "{\"votesAndBlocks\":\"" + stapleBase64 + "\"}";
            List<String> publishErrors = new ArrayList<>();
            for (String api : repApis) {
                try {
                    String response = postJson(api + "/node/publish", body);
                    Matcher publish = PUBLISH_PATTERN.matcher(response);
                    if (publish.find() && Boolean.parseBoolean(publish.group(1))) {
                        return true;
                    }
                    debug("Publish non-success from " + api + ": " + response);
                    publishErrors.add(api + ": non-success response " + response);
                } catch (Exception ignored) {
                    publishErrors.add(api + ": " + ignored.getMessage());
                    debug("Publish failed from " + api + ": " + ignored.getMessage());
                }
            }

            throw new RuntimeException("Publish failed on all representatives: " + String.join(" | ", publishErrors));
        } finally {
            if (feeBlock != null) {
                feeBlock.close();
            }
        }
    }

    private byte[][] toBlockBytes(List<Block.SignedBlock> blocks) {
        byte[][] blockBytes = new byte[blocks.size()][];
        for (int i = 0; i < blocks.size(); i++) {
            blockBytes[i] = blocks.get(i).toBytes();
        }
        return blockBytes;
    }

    private Block.SignedBlock buildFeeBlockIfRequired(List<RepresentativeVote> temporaryVotes, List<Block.SignedBlock> existingBlocks) {
        List<FeeRequirement> fees = new ArrayList<>();
        try (Account baseToken = getBaseToken()) {
            for (RepresentativeVote vote : temporaryVotes) {
                String feeRaw = KeetaNetJNI.voteSelectFee(vote.vote, baseToken.getNativePtr());
                if (feeRaw == null || feeRaw.isEmpty()) {
                    continue;
                }
                String[] parts = feeRaw.split("\n", 3);
                if (parts.length != 3) {
                    throw new RuntimeException("Malformed fee payload from vote parser");
                }
                fees.add(new FeeRequirement(parts[0], parts[1], parts[2]));
            }
        }

        if (fees.isEmpty()) {
            return null;
        }

        byte[] previous = null;
        String signerAccount = signer.publicKeyString();
        for (int i = existingBlocks.size() - 1; i >= 0; i--) {
            Block.SignedBlock candidate = existingBlocks.get(i);
            if (signerAccount.equals(candidate.getAccountPublicKeyString())) {
                previous = candidate.hash();
                break;
            }
        }
        if (previous == null) {
            previous = head();
        }
        List<Account> createdAccounts = new ArrayList<>();
        List<Operation> operations = new ArrayList<>();
        try {
            try (Block.Builder builder = new Block.Builder(networkId, signer, previous)) {
                builder.purpose(Block.PURPOSE_FEE);
                builder.signer(signer);
                for (FeeRequirement fee : fees) {
                    Account to = Account.fromPublicKeyString(fee.payTo);
                    Account token = Account.fromPublicKeyString(fee.token);
                    createdAccounts.add(to);
                    createdAccounts.add(token);
                    Operation send = Operation.createSend(to, token, fee.amount);
                    operations.add(send);
                    builder.addOperation(send);
                }
                try (Block.UnsignedBlock unsigned = builder.seal()) {
                    return unsigned.sign();
                }
            }
        } finally {
            for (Operation operation : operations) {
                operation.close();
            }
            for (Account account : createdAccounts) {
                account.close();
            }
        }
    }

    private static boolean isSuccessorVoteExistsError(RuntimeException error) {
        String message = error.getMessage();
        return message != null && message.contains("LEDGER_SUCCESSOR_VOTE_EXISTS");
    }

    private static boolean isPublishFailedError(RuntimeException error) {
        String message = error.getMessage();
        return message != null && message.contains("Publish failed on all representatives");
    }

    private boolean recoverAccount(String accountPublicKey) {
        debug("Recovering account " + accountPublicKey);
        PendingBlockCandidate pending = getMostCommonPendingBlock(accountPublicKey);
        if (pending == null) {
            debug("No pending block found for account " + accountPublicKey);
            return false;
        }
        debug("Pending block selected hash=" + pending.hash);

        List<byte[]> votes = getSideVotesForHash(pending.hash);
        if (votes.isEmpty()) {
            debug("No side votes found for pending block " + pending.hash);
            return false;
        }
        debug("Collected side votes: " + votes.size());

        byte[] staple = KeetaNetJNI.createVoteStaple(new byte[][] { pending.bytes }, votes.toArray(new byte[0][]));
        if (staple == null || staple.length == 0) {
            return false;
        }

        String stapleBase64 = Base64.getEncoder().encodeToString(staple);
        String body = "{\"votesAndBlocks\":\"" + stapleBase64 + "\"}";
        boolean published = false;
        for (String api : repApis) {
            try {
                String response = postJson(api + "/node/publish", body);
                Matcher publish = PUBLISH_PATTERN.matcher(response);
                if (publish.find() && Boolean.parseBoolean(publish.group(1))) {
                    published = true;
                }
            } catch (Exception ignored) {
                // Continue trying other reps during recovery
            }
        }
        debug("Recovery publish " + (published ? "succeeded" : "failed") + " for account " + accountPublicKey);
        return published;
    }

    private PendingBlockCandidate getMostCommonPendingBlock(String accountPublicKey) {
        List<PendingBlockCandidate> candidates = new ArrayList<>();
        for (String api : repApis) {
            try {
                String path = "/node/ledger/account/" + urlEncode(accountPublicKey) + "/pending";
                String response = callGetFromRep(api, path);
                Matcher hashMatcher = BLOCK_HASH_PATTERN.matcher(response);
                Matcher binaryMatcher = VOTE_BINARY_PATTERN.matcher(response);
                if (hashMatcher.find() && binaryMatcher.find()) {
                    String hash = hashMatcher.group(1);
                    byte[] blockBytes = Base64.getDecoder().decode(binaryMatcher.group(1));
                    candidates.add(new PendingBlockCandidate(hash, blockBytes));
                }
            } catch (Exception ignored) {
                // Ignore individual rep pending lookup failures
            }
        }
        if (candidates.isEmpty()) {
            return null;
        }

        PendingBlockCandidate best = candidates.get(0);
        int bestCount = 0;
        for (PendingBlockCandidate candidate : candidates) {
            int count = 0;
            for (PendingBlockCandidate other : candidates) {
                if (candidate.hash.equals(other.hash)) {
                    count++;
                }
            }
            if (count > bestCount) {
                best = candidate;
                bestCount = count;
            }
        }
        return best;
    }

    private List<byte[]> getSideVotesForHash(String blockHash) {
        List<byte[]> votes = new ArrayList<>();
        for (String api : repApis) {
            try {
                String response = callGetFromRep(api, "/vote/" + blockHash + "?side=side");
                Matcher matcher = VOTE_BINARY_PATTERN.matcher(response);
                if (matcher.find()) {
                    votes.add(Base64.getDecoder().decode(matcher.group(1)));
                }
            } catch (Exception ignored) {
                // Ignore missing side votes on some reps
            }
        }
        return votes;
    }

    private List<RepresentativeVote> requestTemporaryVotes(byte[][] blocks) {
        List<RepresentativeVote> votes = new ArrayList<>();
        List<String> errors = new ArrayList<>();
        String blocksJson = toBase64Array(blocks);
        String body = "{\"blocks\":" + blocksJson + "}";

        for (String api : repApis) {
            try {
                String response = postJson(api + "/vote", body);
                Matcher matcher = VOTE_BINARY_PATTERN.matcher(response);
                if (matcher.find()) {
                    votes.add(new RepresentativeVote(api, Base64.getDecoder().decode(matcher.group(1))));
                } else {
                    errors.add(api + ": no vote field in response");
                }
            } catch (Exception e) {
                errors.add(api + ": " + e.getMessage());
            }
        }
        if (votes.isEmpty() && !errors.isEmpty()) {
            throw new RuntimeException("Vote request failed for all representatives: " + String.join(" | ", errors));
        }
        debug("Temporary votes collected: " + votes.size());
        return votes;
    }

    private List<byte[]> requestPermanentVotes(byte[][] blocks, List<RepresentativeVote> temporaryVotes) {
        List<byte[]> votes = new ArrayList<>();
        String blocksJson = toBase64Array(blocks);
        byte[][] allTemporaryVotes = new byte[temporaryVotes.size()][];
        for (int i = 0; i < temporaryVotes.size(); i++) {
            allTemporaryVotes[i] = temporaryVotes.get(i).vote;
        }
        String votesJson = toBase64Array(allTemporaryVotes);
        for (RepresentativeVote tempVote : temporaryVotes) {
            try {
                String body = "{\"blocks\":" + blocksJson + ",\"votes\":" + votesJson + "}";
                String response = postJson(tempVote.api + "/vote", body);
                Matcher matcher = VOTE_BINARY_PATTERN.matcher(response);
                if (matcher.find()) {
                    votes.add(Base64.getDecoder().decode(matcher.group(1)));
                }
            } catch (Exception ignored) {
                // Continue with other representatives
            }
        }
        debug("Permanent votes collected: " + votes.size());
        return votes;
    }

    private String callGet(String path) {
        RuntimeException last = null;
        for (String api : repApis) {
            try {
                return callGetFromRep(api, path);
            } catch (Exception e) {
                last = new RuntimeException("GET failed for " + api + path + ": " + e.getMessage(), e);
            }
        }
        throw last == null ? new RuntimeException("No representative endpoints available") : last;
    }

    private String callGetFromRep(String api, String path) {
        try {
            HttpRequest request = HttpRequest.newBuilder(URI.create(api + path)).GET().build();
            HttpResponse<String> response = httpClient.send(request, HttpResponse.BodyHandlers.ofString());
            if (response.statusCode() >= 200 && response.statusCode() < 300) {
                return response.body();
            }
            throw new RuntimeException("HTTP " + response.statusCode() + ": " + response.body());
        } catch (Exception e) {
            throw new RuntimeException("GET failed for " + api + path + ": " + e.getMessage(), e);
        }
    }

    private String postJson(String url, String body) {
        try {
            HttpRequest request = HttpRequest.newBuilder(URI.create(url))
                .header("Content-Type", "application/json")
                .POST(HttpRequest.BodyPublishers.ofString(body))
                .build();
            HttpResponse<String> response = httpClient.send(request, HttpResponse.BodyHandlers.ofString());
            if (response.statusCode() < 200 || response.statusCode() >= 300) {
                throw new RuntimeException("HTTP " + response.statusCode() + ": " + response.body());
            }
            return response.body();
        } catch (Exception e) {
            String detail = e.getMessage();
            if (e.getCause() != null && e.getCause().getMessage() != null) {
                detail = detail + " (cause: " + e.getCause().getMessage() + ")";
            }
            throw new RuntimeException("POST failed for " + url + ": " + detail, e);
        }
    }

    private static String toBase64Array(byte[][] data) {
        StringBuilder builder = new StringBuilder("[");
        for (int i = 0; i < data.length; i++) {
            if (i > 0) {
                builder.append(',');
            }
            builder.append('"').append(Base64.getEncoder().encodeToString(data[i])).append('"');
        }
        builder.append(']');
        return builder.toString();
    }

    private static String urlEncode(String value) {
        return URLEncoder.encode(value, StandardCharsets.UTF_8);
    }

    private static byte[] hexToBytes(String hex) {
        if ((hex.length() & 1) != 0) {
            throw new IllegalArgumentException("Invalid hex string length");
        }
        byte[] out = new byte[hex.length() / 2];
        for (int i = 0; i < out.length; i++) {
            int hi = Character.digit(hex.charAt(i * 2), 16);
            int lo = Character.digit(hex.charAt(i * 2 + 1), 16);
            if (hi < 0 || lo < 0) {
                throw new IllegalArgumentException("Invalid hex character");
            }
            out[i] = (byte) ((hi << 4) | lo);
        }
        return out;
    }

    private static void debug(String message) {
        if (DEBUG) {
            System.err.println("[UserClient] " + message);
        }
    }

    @Override
    public void close() {
        // no-op
    }
}
