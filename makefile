.PHONY : dist clean compile publish

DIR_NAME := pdfcrowd-5.1.2
BIN_DIR := bin

compile:
	./build

clean:
	@rm -rf $(BIN_DIR)
	./build -target=Clean

dist:
	@rm -rf dist
	@mkdir -p dist
	@cd dist && mkdir -p $(DIR_NAME) && cp -R ../$(BIN_DIR)/Release/* $(DIR_NAME) && zip -r pdfcrowd.zip $(DIR_NAME)/*

publish:
	sudo nuget update -self
	sudo rm -rf /tmp/NuGetScratch/lock
	nuget pack
	nuget push Pdfcrowd.Official.5.1.2.nupkg -Source https://www.nuget.org/api/v2/package -ApiKey $(API_KEY)
