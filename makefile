COMPILER := mcs
BIN_DIR := bin
OUT := $(BIN_DIR)/pdfcrowd.dll
DOT_NET_VERSION := 2

all: build

build:
	@mkdir -p $(BIN_DIR)
	$(COMPILER) -t:library -sdk:$(DOT_NET_VERSION) /reference:System.Net.dll /reference:System.Web.dll \
		-out:$(OUT) -keyfile:../Pdfcrowd.snk \
		src/AssemblyInfo.cs src/Client.cs src/Error.cs src/Pdfcrowd.cs

dist: