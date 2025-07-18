.PHONY : dist clean compile publish

VERSION = 6.5.2
DIR_NAME := pdfcrowd-6.5.2
BIN_DIR := bin

compile:
	./build

clean:
	@rm -rf $(BIN_DIR)
	./build -target=Clean

dist: dist/pdfcrowd-$(VERSION)-dotnet.zip

dist/pdfcrowd-$(VERSION)-dotnet.zip:
	@rm -rf dist
	@mkdir -p dist
	@cd dist && mkdir -p $(DIR_NAME) && cp -R ../$(BIN_DIR)/Release/* $(DIR_NAME) && zip -r pdfcrowd-$(VERSION)-dotnet.zip $(DIR_NAME)/*

publish:
	sudo nuget update -self
	sudo rm -rf /tmp/NuGetScratch/lock
	nuget pack
	nuget push Pdfcrowd.Official.6.5.2.nupkg -Source https://www.nuget.org/api/v2/package -ApiKey $(API_KEY)
