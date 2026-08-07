.PHONY: help build restore clean do-lint

CONFIG ?= Release
SLN := KeetaNet.Examples.slnx
PROJECT := src/KeetaNet.Examples.csproj
ANCHOR_CSHARP := ../../../anchor-csharp

EXAMPLES := \
	client/accounts-create \
	client/token-management \
	client/send-and-listen \
	anchor/encrypted-container \
	anchor/kyc-client \
	anchor/kyc-client-sharekyc \
	anchor/asset-movement-evm-inbound \
	anchor/asset-movement-evm-outbound \
	anchor/asset-movement-fiat-deposit-from-crypto \
	anchor/asset-movement-fiat-deposit-from-bank \
	anchor/asset-movement-fiat-withdraw-to-bank \
	anchor/asset-movement-persistent-address

default: help

help:
	@echo "Usage: make [target]"
	@echo ""
	@echo "Targets:"
	@echo "  build          - Build the examples runner"
	@echo "  do-lint        - Format C# and check spelling"
	@echo "  help           - List runnable examples (same ids as keetanet-examples TypeScript)"
	@for example in $(EXAMPLES); do \
		printf '  %-46s - run example\n' "$$example"; \
	done

restore:
	dotnet restore $(SLN)

build: restore
	dotnet build $(SLN) -c $(CONFIG) --no-restore

do-lint:
	dotnet format $(SLN)
	npx --yes cspell --config cspell.yaml --no-progress "src/**/*.cs" "Makefile" "README.md"

$(EXAMPLES): build
	dotnet run --project $(PROJECT) -c $(CONFIG) --no-build -- $@ $(ARGS)

clean:
	dotnet clean $(SLN) -c $(CONFIG)
