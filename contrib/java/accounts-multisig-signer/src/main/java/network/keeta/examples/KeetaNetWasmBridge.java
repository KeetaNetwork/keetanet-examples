package network.keeta.examples;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.SecureRandom;
import java.time.Clock;

import com.dylibso.chicory.runtime.ExportFunction;
import com.dylibso.chicory.runtime.ImportValues;
import com.dylibso.chicory.runtime.Instance;
import com.dylibso.chicory.runtime.Memory;
import com.dylibso.chicory.wasi.WasiOptions;
import com.dylibso.chicory.wasi.WasiPreview1;
import com.dylibso.chicory.wasm.Parser;
import com.dylibso.chicory.wasm.WasmModule;

final class KeetaNetWasmBridge {
    private final Instance instance;
    private final Memory memory;

    KeetaNetWasmBridge() {
        try {
            String explicit = System.getenv("KEETANET_WASM_PATH");
            Path wasmPath = explicit == null || explicit.isBlank()
                ? Path.of("target", "wasm", "keetanet_wasm_bridge.wasm")
                : Path.of(explicit);
            if (!Files.exists(wasmPath)) {
                throw new IllegalStateException("WASM module not found: " + wasmPath);
            }

            WasmModule module = Parser.parse(wasmPath);
            WasiOptions wasiOptions = WasiOptions.builder()
                .withRandom(new SecureRandom())
                .withClock(Clock.systemUTC())
                .build();
            WasiPreview1 wasi = WasiPreview1.builder()
                .withOptions(wasiOptions)
                .build();
            ImportValues imports = ImportValues.builder()
                .addFunction(wasi.toHostFunctions())
                .build();
            this.instance = Instance.builder(module)
                .withImportValues(imports)
                .build();
            this.memory = instance.memory();
        } catch (Exception e) {
            throw new IllegalStateException("Failed to load WASM module", e);
        }
    }

    long callI64(String fn, long... args) {
        ExportFunction function = instance.export(fn);
        long[] out = function.apply(args);
        if (out == null || out.length < 1) {
            throw new IllegalStateException("WASM function returned no values: " + fn);
        }
        return out[0];
    }

    void callVoid(String fn, long... args) {
        ExportFunction function = instance.export(fn);
        function.apply(args);
    }

    int callI32(String fn, long... args) {
        return (int) callI64(fn, args);
    }

    long alloc(int len) {
        return Integer.toUnsignedLong(callI32("kn_alloc", Integer.toUnsignedLong(len)));
    }

    void free(long ptr, int len) {
        callVoid("kn_free", ptr, Integer.toUnsignedLong(len));
    }

    long allocAndWrite(byte[] bytes) {
        if (bytes == null || bytes.length == 0) {
            return 0;
        }
        long ptr = alloc(bytes.length);
        memory.write((int) ptr, bytes);
        return ptr;
    }

    long allocAndWriteU64Array(long[] values) {
        if (values == null || values.length == 0) {
            return 0;
        }
        int len = values.length * Long.BYTES;
        long ptr = alloc(len);
        ByteBuffer bb = ByteBuffer.allocate(len).order(ByteOrder.LITTLE_ENDIAN);
        for (long value : values) {
            bb.putLong(value);
        }
        memory.write((int) ptr, bb.array());
        return ptr;
    }

    long allocAndWritePtrLenArray(long[][] values) {
        if (values == null || values.length == 0) {
            return 0;
        }
        int len = values.length * Integer.BYTES * 2;
        long ptr = alloc(len);
        ByteBuffer bb = ByteBuffer.allocate(len).order(ByteOrder.LITTLE_ENDIAN);
        for (long[] value : values) {
            bb.putInt((int) value[0]);
            bb.putInt((int) value[1]);
        }
        memory.write((int) ptr, bb.array());
        return ptr;
    }

    byte[] readPackedBytes(long packed) {
        int ptr = (int) (packed >>> 32);
        int len = (int) packed;
        if (ptr == 0 || len <= 0) {
            return null;
        }
        byte[] out = memory.readBytes(ptr, len);
        free(Integer.toUnsignedLong(ptr), len);
        return out;
    }

    String readPackedUtf8(long packed) {
        byte[] bytes = readPackedBytes(packed);
        if (bytes == null) {
            return null;
        }
        return new String(bytes, StandardCharsets.UTF_8);
    }

    byte[] callBytes(String fn, long... args) {
        long packed = callI64(fn, args);
        if (packed == 0) {
            return null;
        }
        return readPackedBytes(packed);
    }

    String callString(String fn, long... args) {
        long packed = callI64(fn, args);
        if (packed == 0) {
            return null;
        }
        return readPackedUtf8(packed);
    }

    String lastError() {
        long packed = callI64("kn_last_error");
        String message = readPackedUtf8(packed);
        return message == null ? "unknown native error" : message;
    }
}
