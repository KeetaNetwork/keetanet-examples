# Makefile for Keeta Network Java WASM multisig example

SRC_JAVA = src/main/java
SRC_WASM = wasm-bridge
TARGET = target
TARGET_CLASSES = $(TARGET)/classes
TARGET_WASM = $(TARGET)/wasm
TARGET_CHICORY = $(TARGET)/chicory
PWD_REAL := $(shell pwd -P)

WASM_FILE          := $(TARGET_WASM)/keetanet_wasm_bridge.wasm
KEETANET_WASM_PATH := $(PWD_REAL)/$(WASM_FILE)
MAIN_CLASS         := network.keeta.examples.AccountsMultisigSigner

CHICORY_VERSION = 1.7.5
CHICORY_RUNTIME_JAR = $(TARGET_CHICORY)/runtime-$(CHICORY_VERSION).jar
CHICORY_RUNTIME_SHA256 = cdbb2bd4e353eacff78da5dd3454694316eaa6d2950aab3d1517dca41ae4a0b2
CHICORY_WASM_JAR = $(TARGET_CHICORY)/wasm-$(CHICORY_VERSION).jar
CHICORY_WASM_SHA256 = 16558a66bac04f7e00ea06950f52d87ce2cfff535a54bac2115f2c13e0b3050b
CHICORY_WASI_JAR = $(TARGET_CHICORY)/wasi-$(CHICORY_VERSION).jar
CHICORY_WASI_SHA256 = 52f68c155b1dc4ff6df9a6b9eb84201029886561e87bc77a858bd14333c1cfbc
CHICORY_LOG_JAR = $(TARGET_CHICORY)/log-$(CHICORY_VERSION).jar
CHICORY_LOG_SHA256 = 79e4e505060d48dee4f520daa9591dbb9b9dc2e3073e1a5fcb2018bfd2d5d1f2

CHICORY_CLASS_PATH = $(CHICORY_RUNTIME_JAR):$(CHICORY_WASM_JAR):$(CHICORY_WASI_JAR):$(CHICORY_LOG_JAR)

JAVA_SOURCES = $(wildcard $(SRC_JAVA)/network/keeta/examples/*.java)
JAVA_CLASSES = $(patsubst $(SRC_JAVA)/%.java,$(TARGET_CLASSES)/%.class,$(JAVA_SOURCES))
HIGH_LEVEL_ARTIFACTS = $(WASM_FILE) $(JAVA_CLASSES)

all: $(HIGH_LEVEL_ARTIFACTS)

help:
	@echo "Keeta Network Java WASM Example - Build System"
	@echo ""
	@echo "Description: Example of using Keeta Network Rust crates from Java via WASM (Chicory)"
	@echo ""
	@echo "Targets:"
	@echo "  all          - Build WASM bridge and Java classes (default)"
	@echo "  run          - Build and run the example"
	@echo "  clean        - Remove Java build artifacts"
	@echo "  distclean    - Remove all build artifacts"
	@echo "  test         - Run basic functionality test"
	@echo "  help         - Show this help message"
	@echo ""
	@echo "Requirements:"
	@echo "  - Rust 1.70+ (cargo + wasm32-wasip1 target)"
	@echo "  - Java JDK 17+"
	@echo "  - curl"
	@echo "  - Make"

$(TARGET_CHICORY):
	@mkdir -p $(TARGET_CHICORY)

$(CHICORY_RUNTIME_JAR): | $(TARGET_CHICORY)
	@tmp="$(CHICORY_RUNTIME_JAR).tmp"; \
	rm -f "$$tmp"; \
	curl -sSfL -o "$$tmp" https://repo1.maven.org/maven2/com/dylibso/chicory/runtime/$(CHICORY_VERSION)/runtime-$(CHICORY_VERSION).jar && \
	echo "$(CHICORY_RUNTIME_SHA256)  $$tmp" | sha256sum -c - && \
	mv "$$tmp" "$(CHICORY_RUNTIME_JAR)"

$(CHICORY_WASM_JAR): | $(TARGET_CHICORY)
	@tmp="$(CHICORY_WASM_JAR).tmp"; \
	rm -f "$$tmp"; \
	curl -sSfL -o "$$tmp" https://repo1.maven.org/maven2/com/dylibso/chicory/wasm/$(CHICORY_VERSION)/wasm-$(CHICORY_VERSION).jar && \
	echo "$(CHICORY_WASM_SHA256)  $$tmp" | sha256sum -c - && \
	mv "$$tmp" "$(CHICORY_WASM_JAR)"

$(CHICORY_WASI_JAR): | $(TARGET_CHICORY)
	@tmp="$(CHICORY_WASI_JAR).tmp"; \
	rm -f "$$tmp"; \
	curl -sSfL -o "$$tmp" https://repo1.maven.org/maven2/com/dylibso/chicory/wasi/$(CHICORY_VERSION)/wasi-$(CHICORY_VERSION).jar && \
	echo "$(CHICORY_WASI_SHA256)  $$tmp" | sha256sum -c - && \
	mv "$$tmp" "$(CHICORY_WASI_JAR)"

$(CHICORY_LOG_JAR): | $(TARGET_CHICORY)
	@tmp="$(CHICORY_LOG_JAR).tmp"; \
	rm -f "$$tmp"; \
	curl -sSfL -o "$$tmp" https://repo1.maven.org/maven2/com/dylibso/chicory/log/$(CHICORY_VERSION)/log-$(CHICORY_VERSION).jar && \
	echo "$(CHICORY_LOG_SHA256)  $$tmp" | sha256sum -c - && \
	mv "$$tmp" "$(CHICORY_LOG_JAR)"

$(TARGET_WASM):
	@mkdir -p $(TARGET_WASM)

$(WASM_FILE): $(SRC_WASM)/Cargo.toml $(wildcard $(SRC_WASM)/src/*.rs) | $(TARGET_WASM)
	rustup target add wasm32-wasip1 >/dev/null 2>&1 || true
	cd $(SRC_WASM) && cargo build --release --target wasm32-wasip1
	cp $(SRC_WASM)/target/wasm32-wasip1/release/keetanet_wasm_bridge.wasm $(WASM_FILE)
	@test -f $(WASM_FILE) || (echo "Error: WASM module not built at $(WASM_FILE)" && exit 1)

$(TARGET_CLASSES):
	mkdir -p $(TARGET_CLASSES)

$(TARGET_CLASSES)/%.class: $(SRC_JAVA)/%.java | $(TARGET_CLASSES) $(CHICORY_RUNTIME_JAR) $(CHICORY_WASM_JAR) $(CHICORY_WASI_JAR) $(CHICORY_LOG_JAR)
	javac -cp $(CHICORY_CLASS_PATH) -d $(TARGET_CLASSES) $(JAVA_SOURCES)

run: $(HIGH_LEVEL_ARTIFACTS)
	KEETANET_WASM_PATH=$(KEETANET_WASM_PATH) \
	java --enable-native-access=ALL-UNNAMED -cp $(TARGET_CLASSES):$(CHICORY_CLASS_PATH) $(MAIN_CLASS)

test: $(HIGH_LEVEL_ARTIFACTS)
	KEETANET_WASM_PATH=$(KEETANET_WASM_PATH) \
	java --enable-native-access=ALL-UNNAMED -cp $(TARGET_CLASSES):$(CHICORY_CLASS_PATH) $(MAIN_CLASS) > /dev/null 2>&1 \
	&& echo "✓ Test passed" || (echo "✗ Test failed" && exit 1)

clean:
	rm -rf $(TARGET_CLASSES)

distclean: clean
	rm -rf $(TARGET)
	cd $(SRC_WASM) && cargo clean

.PHONY: all clean distclean run help test
