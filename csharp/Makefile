.PHONY: help build restore clean

CONFIG ?= Release
SLN := KeetaNet.Examples.slnx
PROJECT := src/KeetaNet.Examples.csproj
ANCHOR_CSHARP := ../../../anchor-csharp

EXAMPLES := \
	anchor/kyc-client \
	anchor/kyc-client-sharekyc \
	anchor/asset-movement-evm-inbound \
	anchor/asset-movement-evm-outbound \
	anchor/asset-movement-fiat-deposit-from-crypto

default: help

help:
	@echo "Usage: make [target]"
	@echo ""
	@echo "Targets:"
	@echo "  build          - Build the examples runner"
	@echo "  help           - List runnable examples (same ids as keetanet-examples TypeScript)"
	@for example in $(EXAMPLES); do \
		printf '  %-46s - run example\n' "$$example"; \
	done

restore:
	dotnet restore $(SLN)

build: restore
	dotnet build $(SLN) -c $(CONFIG) --no-restore

$(EXAMPLES): build
	dotnet run --project $(PROJECT) -c $(CONFIG) --no-build -- $@ $(ARGS)

clean:
	dotnet clean $(SLN) -c $(CONFIG)
