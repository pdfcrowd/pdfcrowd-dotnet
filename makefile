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

RSYNC_ARGS := -avz --cvs-exclude --exclude "bin/*" --exclude "dist/*" --exclude "test_files" .

all: test

test:
	rsync $(RSYNC_ARGS) $(WIN_HOST):/tmp/csharp_client_build
	rsync -avz --cvs-exclude ../test_files/in/ $(WIN_HOST):/tmp/csharp_client_build/test_files/in/
	ssh $(WIN_HOST) rm -f /tmp/csharp_client_build/test_files/out/*
	test -f ~/crowdenv/$(API_ENV_FILE) || echo "API_ENV_FILE not defined"
	ssh $(WIN_HOST)	'test -f ~/crowdenv/$(API_ENV_FILE) || (echo "API_ENV_FILE not found" && false)' 
	ssh $(WIN_HOST) 'source ~/crowdenv/$(API_ENV_FILE) && make $@ -f makefile.cygwin -C /tmp/csharp_client_build'
	rsync -avz --cvs-exclude "$(WIN_HOST):/tmp/csharp_client_build/test_files/out/cs_client*" ../test_files/out/

dist:
	rsync $(RSYNC_ARGS) $(WIN_HOST):/tmp/csharp_client_build
	ssh $(WIN_HOST) make $@ -f makefile.cygwin -C /tmp/csharp_client_build
	mkdir -p dist
	rsync $(WIN_HOST):/tmp/csharp_client_build/dist/*.zip dist/

init:
	test -d ../test_files/out || mkdir -p ../test_files/out
	test -e test_files || ln -s ../test_files/ test_files


clean:
	rsync $(RSYNC_ARGS) $(WIN_HOST):/tmp/csharp_client_build
	ssh $(WIN_HOST) make $@ -f makefile.cygwin -C /tmp/csharp_client_build
	rm -rf dist/* test_files/out/cs_*.pdf

endif

