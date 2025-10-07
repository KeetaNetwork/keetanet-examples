TS_FILES := $(shell find src -name '*.ts' ! -path 'src/helper.ts' | sort)
RUNNABLE := $(patsubst src/%.ts,%,$(TS_FILES))

all:
	@echo 'Nothing to build, please run "make help" for available commands.'

# This target provides a list of targets.
help:
	@echo 'Usage: make [target]'
	@echo ''
	@echo 'Targets:'
	@for file in $(TS_FILES); do \
		target=$${file#src/}; \
		target=$${target%.ts}; \
		desc=$$(grep ' Description:' "$$file" | sed 's/^.* Description: //'); \
		printf '  %-40s - %s\n' "$$target" "$$desc"; \
	done

node_modules/.done: package.json
	rm -rf node_modules
	npm ci
	@touch node_modules/.done

node_modules: node_modules/.done
	@touch node_modules

# Run linting
do-lint: node_modules
	npm run eslint -- --config .eslint.config.mjs src ${KEETANET_EXAMPLES_LINT_ARGS}

$(RUNNABLE): node_modules
	npx tsx 'src/$@'

.PHONY: all help node_modules $(RUNNABLE)

