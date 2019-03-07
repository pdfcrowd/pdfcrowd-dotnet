.PHONY : dist clean

DIR_NAME := pdfcrowd-4.6.0
COMPILER := mcs
BIN_DIR := bin
OUT := $(BIN_DIR)/pdfcrowd.dll
DOT_NET_VERSION := 2

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
	sudo nuget update -self
	nuget pack
	nuget push Pdfcrowd.Official.4.6.0.nupkg -Source https://www.nuget.org/api/v2/package -ApiKey $(API_KEY)
