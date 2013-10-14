-include .config.makefile

ifeq (,$(WIN_HOST))
ifeq (,$(WIN_HOST_OPTIONAL))
$(error Windows host machine WIN_HOST not specified. Use WIN_HOST_OPTIONAL=1 to ignore.)
else
do_nothing := 1
endif
endif

ping_res := $(shell ping -c 1 -W 1 $(WIN_HOST))
ifeq (,$(findstring 1 received, $(ping_res)))
ifeq (,$(WIN_HOST_OPTIONAL))
$(error Windows host machine $(WIN_HOST) not responding. Use WIN_HOST_OPTIONAL=1 to ignore.)
else
do_nothing := 1
endif
endif


.PHONY: all dist test init clean

ifeq (1,$(do_nothing))
$(warning WARNING: .NET makefile does nothing)
else

ifeq (,$(WIN_USERNAME))
WIN_CONN := $(WIN_HOST)
else
WIN_CONN := $(WIN_USERNAME)@$(WIN_HOST)
endif

RSYNC_ARGS := -avz --cvs-exclude --exclude "bin/*" --exclude "dist/*" --exclude "test_files" .
CMDENV = API_USERNAME=$(API_USERNAME) API_TOKEN=$(API_TOKEN) API_HOSTNAME=$(API_HOSTNAME) API_HTTP_PORT=$(API_HTTP_PORT) API_HTTPS_PORT=$(API_HTTPS_PORT)

all: test


test:
	rsync $(RSYNC_ARGS) $(WIN_CONN):/tmp/csharp_client_build
	rsync -avz --cvs-exclude ../test_files/in/ $(WIN_CONN):/tmp/csharp_client_build/test_files/in/
	ssh $(WIN_CONN) rm -f /tmp/csharp_client_build/test_files/out/*
	ssh $(WIN_CONN) $(CMDENV) 'make $@ -f makefile.cygwin -C /tmp/csharp_client_build'
	rsync -avz --cvs-exclude "$(WIN_CONN):/tmp/csharp_client_build/test_files/out/cs_client*" ../test_files/out/

dist:
	rsync $(RSYNC_ARGS) $(WIN_CONN):/tmp/csharp_client_build
	ssh $(WIN_CONN) make $@ -f makefile.cygwin -C /tmp/csharp_client_build
	mkdir -p dist
	rsync $(WIN_CONN):/tmp/csharp_client_build/dist/*.zip dist/

init:
	test -d ../test_files/out || mkdir -p ../test_files/out
	test -e test_files || ln -s ../test_files/ test_files

clean:
	rsync $(RSYNC_ARGS) $(WIN_CONN):/tmp/csharp_client_build
	ssh $(WIN_CONN) make $@ -f makefile.cygwin -C /tmp/csharp_client_build
	rm -rf dist/* test_files/out/cs_*.pdf

endif




