# Makefile for Keetanet Java JNI Example
#
# This Makefile builds the Java JNI example that demonstrates using
# Keetanetwork Rust crates from Java via JNI.

# Detect OS for library extension
UNAME_S := $(shell uname -s)
ifeq ($(UNAME_S),Linux)
    LIB_EXT = so
    LIB_PREFIX = lib
endif
ifeq ($(UNAME_S),Darwin)
    LIB_EXT = dylib
    LIB_PREFIX = lib
endif
ifeq ($(OS),Windows_NT)
    LIB_EXT = dll
    LIB_PREFIX =
endif

# Directories
SRC_JAVA = src/main/java
SRC_RUST = src/main/rust
TARGET = target
TARGET_CLASSES = $(TARGET)/classes
TARGET_RUST = $(TARGET)/release

# Java source files
JAVA_SOURCES = $(wildcard $(SRC_JAVA)/network/keeta/examples/*.java)
JAVA_CLASSES = $(patsubst $(SRC_JAVA)/%.java,$(TARGET_CLASSES)/%.class,$(JAVA_SOURCES))

# Rust source files
RUST_SOURCES = $(wildcard $(SRC_RUST)/*.rs) $(SRC_RUST)/../../../Cargo.toml

# Native library
NATIVE_LIB = $(TARGET_RUST)/$(LIB_PREFIX)keetanet_jni_multisig.$(LIB_EXT)

# Main class
MAIN_CLASS = network.keeta.examples.AccountsMultisigSigner

.PHONY: all clean distclean run help test

# Default target
all: $(NATIVE_LIB) $(JAVA_CLASSES)

help:
	@echo "Keetanet Java JNI Example - Build System"
	@echo ""
	@echo "Description: Example of using Keeta Network Rust crates via JNI from Java"
	@echo ""
	@echo "Targets:"
	@echo "  all          - Build Rust library and Java classes (default)"
	@echo "  run          - Build and run the example"
	@echo "  clean        - Remove Java build artifacts"
	@echo "  distclean    - Remove all build artifacts including Rust"
	@echo "  test         - Run basic functionality test"
	@echo "  help         - Show this help message"
	@echo ""
	@echo "Requirements:"
	@echo "  - Rust 1.70+ (cargo)"
	@echo "  - Java JDK 17+"
	@echo "  - Make"

# Build the Rust JNI library
$(NATIVE_LIB): $(RUST_SOURCES)
	@echo "Building Rust JNI library..."
	cd $(SRC_RUST) && cargo build --release
	@test -f $(NATIVE_LIB) || (echo "Error: Native library not built at $(NATIVE_LIB)" && exit 1)

# Compile Java sources
$(TARGET_CLASSES)/%.class: $(SRC_JAVA)/%.java | $(TARGET_CLASSES)
	@echo "Compiling Java sources..."
	javac -d $(TARGET_CLASSES) $(JAVA_SOURCES)

$(TARGET_CLASSES):
	@mkdir -p $(TARGET_CLASSES)

# Run the example
run: all
	@echo "Running Keetanet Java JNI Example..."
	@echo ""
	java -Djava.library.path=$(TARGET_RUST) \
	     --enable-native-access=ALL-UNNAMED \
	     -cp $(TARGET_CLASSES) \
	     $(MAIN_CLASS)

# Test that basic functionality works
test: all
	@echo "Testing basic functionality..."
	@java -Djava.library.path=$(TARGET_RUST) \
	      --enable-native-access=ALL-UNNAMED \
	      -cp $(TARGET_CLASSES) \
	      $(MAIN_CLASS) > /dev/null 2>&1 && echo "✓ Test passed" || (echo "✗ Test failed" && exit 1)

# Clean Java build artifacts only
clean:
	@echo "Cleaning Java build artifacts..."
	rm -rf $(TARGET_CLASSES)

# Clean all build artifacts including Rust
distclean: clean
	@echo "Cleaning all build artifacts..."
	rm -rf $(TARGET)
	cd $(SRC_RUST) && cargo clean

