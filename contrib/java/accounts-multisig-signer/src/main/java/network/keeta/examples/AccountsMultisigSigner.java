package network.keeta.examples;

import java.math.BigInteger;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.Base64;

/**
 * Java/JNI equivalent of {@code src/client/accounts-multisig-signer.ts}.
 * It requests faucet funds (test network), builds and transmits both blocks,
 * and prints the same summary values.
 */
public class AccountsMultisigSigner {
    private static final String NETWORK = "test";
    private static final long NETWORK_TEST = 0x54455354L; // 'test'
    private static final int ADJUST_METHOD_SET = 2;

    public static void main(String[] args) {
        try {
            final String seed = Account.generateRandomSeed();
            try (Account userAccount = Account.fromSeed(seed, 0)) {
                if (!getFaucetTokens(userAccount, NETWORK)) {
                    throw new RuntimeException("Failed to get tokens from faucet");
                }

                try (UserClient userClient = UserClient.fromNetwork(NETWORK, userAccount)) {
                    byte[] userAccountHeadBlockHash = userClient.head();

                    try (Account signer1 = Account.fromSeed(seed, 1);
                         Account signer2 = Account.fromSeed(seed, 2);
                         Account signer3 = Account.fromSeed(seed, 3);
                         Account multisigIdentifier = userAccount.generateIdentifier(
                             Account.AccountKeyAlgorithm.MULTISIG, null, 0)) {

                        try (Block.Builder builder = new Block.Builder(NETWORK_TEST, userAccount, userAccountHeadBlockHash);
                             Operation createIdOp = Operation.createMultisigIdentifier(multisigIdentifier, signer1, signer2, signer3, 2);
                             Operation modifyOp = Operation.modifyPermissions(multisigIdentifier, Permissions.ADMIN, ADJUST_METHOD_SET);
                             Block.UnsignedBlock unsigned = buildIdentifierBlock(builder, createIdOp, modifyOp);
                             Block.SignedBlock identifierBlock = unsigned.sign()) {

                            if (!userClient.transmit(identifierBlock)) {
                                throw new RuntimeException("Failed to transmit identifier block");
                            }

                            try (Account customToken = userClient.generateIdentifier(Account.AccountKeyAlgorithm.TOKEN)) {
                                byte[] permissionsPrevious = userClient.head(customToken);
                                try (Block.Builder permissionsBuilder = new Block.Builder(NETWORK_TEST, customToken, permissionsPrevious);
                                     Operation grantAdmin = Operation.modifyPermissions(multisigIdentifier, Permissions.ADMIN, ADJUST_METHOD_SET)) {
                                    permissionsBuilder.signer(userAccount);
                                    permissionsBuilder.addOperation(grantAdmin);
                                    try (Block.UnsignedBlock permissionsUnsigned = permissionsBuilder.seal();
                                         Block.SignedBlock permissionsBlock = permissionsUnsigned.sign()) {
                                        if (!userClient.transmit(permissionsBlock)) {
                                            throw new RuntimeException("Failed to update permissions on custom token");
                                        }
                                    }
                                }

                                byte[] tokenHeadBlockHash = userClient.head(customToken);
                                String basicMetadata = Base64.getEncoder().encodeToString(
                                    "{\"decimalPlaces\":6}".getBytes(StandardCharsets.UTF_8)
                                );

                                try (Block.Builder tokenBuilder = new Block.Builder(NETWORK_TEST, customToken, tokenHeadBlockHash);
                                     Operation setInfoOp = Operation.setInfo(
                                         "TKNM",
                                         "Test Multisig Token Example",
                                         basicMetadata,
                                         Permissions.ACCESS
                                     )) {
                                    tokenBuilder.signer(multisigIdentifier, new Account[]{signer1, signer2});
                                    tokenBuilder.addOperation(setInfoOp);
                                    try (Block.UnsignedBlock tokenUnsigned = tokenBuilder.seal();
                                         Block.SignedBlock multisigExampleBlock = tokenUnsigned.sign()) {
                                        if (!userClient.transmit(multisigExampleBlock)) {
                                            throw new RuntimeException("Failed to transmit multisig example block");
                                        }

                                        System.out.println("Seed: " + seed);
                                        System.out.println("User Account: " + userAccount.publicKeyString());
                                        System.out.println("MultiSig Account: " + multisigIdentifier.publicKeyString());
                                        System.out.println("Create MultiSig Block: " + identifierBlock.getHashHex());
                                        System.out.println("Custom Token: " + customToken.publicKeyString());
                                        System.out.println("Token MultiSig Block: " + multisigExampleBlock.getHashHex());
                                    }
                                }
                            }
                        }
                    }
                }
            }
        } catch (Exception e) {
            System.err.println(e.getMessage());
            e.printStackTrace();
            System.exit(1);
        }
    }

    private static Block.UnsignedBlock buildIdentifierBlock(Block.Builder builder, Operation createIdOp, Operation modifyOp) {
        builder.addOperation(createIdOp);
        builder.addOperation(modifyOp);
        return builder.seal();
    }

    private static boolean getFaucetTokens(Account account, String network) throws Exception {
        if (!"test".equals(network)) {
            throw new IllegalArgumentException("Faucet is Only Available on the Test Network");
        }

        HttpClient httpClient = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(10)).build();
        try (UserClient tempUserClient = UserClient.fromNetwork(network, null);
             Account baseToken = tempUserClient.getBaseToken()) {
            BigInteger initialBalance = tempUserClient.getBalance(account, baseToken);

            String form = "address=" + URLEncoder.encode(account.publicKeyString(), StandardCharsets.UTF_8)
                + "&amount=5";
            HttpRequest request = HttpRequest.newBuilder(URI.create("https://faucet.test.keeta.com"))
                .header("Content-Type", "application/x-www-form-urlencoded")
                .POST(HttpRequest.BodyPublishers.ofString(form))
                .build();
            httpClient.send(request, HttpResponse.BodyHandlers.discarding());

            BigInteger expected = initialBalance.add(BigInteger.valueOf(5));
            for (int i = 0; i < 120; i++) {
                BigInteger balance = tempUserClient.getBalance(account, baseToken);
                if (balance.compareTo(expected) >= 0) {
                    return true;
                }
                Thread.sleep(500);
            }
        }

        return false;
    }
}
