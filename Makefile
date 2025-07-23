all:
	@echo 'Nothing to build, please run "make help" for available commands.'

# This target provides a list of targets.
help:
	@echo 'Usage: make [target]'
	@echo ''
	@echo 'Targets:'
	@for file in $(wildcard *.ts); do \
		desc=$$(grep ' Description:' $${file} | sed 's/^.* Description: //'); \
		printf '  %-12s - %s\n' "$$(basename "$${file}" .ts)" "$${desc}"; \
	done

node_modules/.done: package.json package-lock.json
	rm -rf node_modules
	npm ci
	@touch node_modules/.done

node_modules: node_modules/.done
	@touch node_modules

$(subst .ts,,$(wildcard *.ts)): node_modules
	npm run '$@'

.PHONY: all help example%
