.PHONY : dist clean

DIR_NAME := pdfcrowd-4.4.2
COMPILER := mcs
BIN_DIR := bin
OUT := $(BIN_DIR)/pdfcrowd.dll
DOT_NET_VERSION := 2

NUGET_VALID := $(shell aptitude show nuget | grep -q 'Version: 4' ; echo $$?)

compile:
	@mkdir -p $(BIN_DIR)
	$(COMPILER) -t:library -sdk:$(DOT_NET_VERSION) /reference:System.Net.dll /reference:System.Web.dll \
		-out:$(OUT) -keyfile:../Pdfcrowd.snk \
		src/AssemblyInfo.cs src/Client.cs src/Error.cs src/Pdfcrowd.cs

clean:
	@rm -rf dist
	@rm -rf $(BIN_DIR)

dist:
	@mkdir -p dist
	@cd dist && mkdir -p $(DIR_NAME) && cp ../$(BIN_DIR)/pdfcrowd.dll $(DIR_NAME) && zip pdfcrowd.zip $(DIR_NAME)/*

publish:
	nuget pack
	@if [ $(NUGET_VALID) -eq 0 ]; then \
		nuget push Pdfcrowd.Official.4.4.2.nupkg -Source https://www.nuget.org/api/v2/package -ApiKey $(API_KEY) ; \
	else \
		echo ; \
		echo Not valid nuget version, upload Pdfcrowd.Official.4.4.2.nupkg manually to https://www.nuget.org/packages/manage/upload ; \
		echo ; \
	fi
