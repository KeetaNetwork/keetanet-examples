TS_FILES := $(shell find src -name '*.ts' ! -path 'src/helper.ts')
TARGETS := $(patsubst src/%.ts,%,$(TS_FILES))

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

.PHONY: all help node_modules $(TARGETS)

# Pattern rule must be at the end to avoid catching other rules like 'Makefile' or 'help'
$(TARGETS): %: node_modules
	npm run '$@'
