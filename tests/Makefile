CC ?= cc
CFLAGS ?= -std=c11 -Wall -Wextra -Werror -I../include

.PHONY: test clean

test: file_extents_test contiguous_write_test managed_runtime_tests fst_test card_transaction_test aram_test overlay_test overlay_debug_test debug_heartbeat_test network_link_test worker_test stw_test input_native_test input_overlay_test input_gamecube_test hardware_gamecube_test disc_mount_test dol_test
	./file_extents_test
	./contiguous_write_test
	./managed_runtime_tests
	./fst_test
	./card_transaction_test
	./aram_test
	./overlay_test
	./overlay_debug_test
	./debug_heartbeat_test
	./network_link_test
	./worker_test
	./stw_test
	./input_native_test
	./input_overlay_test
	./input_gamecube_test
	./hardware_gamecube_test
	./disc_mount_test
	./dol_test

file_extents_test: file_extents_test.c ../source/file_extents.c
	$(CC) $(CFLAGS) $^ -o $@

contiguous_write_test: contiguous_write_test.c ../source/contiguous_write.c
	$(CC) $(CFLAGS) $^ -o $@

dol_test: dol_test.c ../source/dol.c ../source/managed_runtime.c ../source/stw.c ../source/gc_stw.c
	$(CC) $(CFLAGS) -pthread $^ -o $@

disc_mount_test: disc_mount_test.c ../source/disc_gamecube.c ../source/fst_gamecube.c ../source/fst.c ../source/iso_file.c
	$(CC) $(CFLAGS) -D_DEFAULT_SOURCE -DDND_GAMECUBE_STORAGE -Idisc_sdk $^ -o $@

hardware_gamecube_test: hardware_gamecube_test.c ../source/hardware.c
	$(CC) $(CFLAGS) -DDND_GAMECUBE_STORAGE -Ihardware_sdk $^ -o $@

input_gamecube_test: input_gamecube_test.c ../source/input_gamecube.c
	$(CC) $(CFLAGS) -Iinput_sdk $^ -o $@

input_overlay_test: input_native_test.c input_test_backend.c ../source/input.c ../source/performance.c
	$(CC) $(CFLAGS) -DDND_SAMPLE_CONTROLS $^ -lm -o $@

input_native_test: input_native_test.c input_test_backend.c ../source/input.c
	$(CC) $(CFLAGS) $^ -lm -o $@

managed_runtime_tests: managed_runtime_tests.c ../source/managed_runtime.c ../source/bcl.c ../source/stw.c ../source/gc_stw.c
	$(CC) $(CFLAGS) -pthread $^ -o $@

fst_test: fst_test.c ../source/fst.c
	$(CC) $(CFLAGS) $^ -o $@

card_transaction_test: card_transaction_test.c ../source/card_transaction.c
	$(CC) $(CFLAGS) $^ -o $@

aram_test: aram_test.c ../source/aram.c
	$(CC) $(CFLAGS) -DDND_ARAM_TEST $^ -o $@

stw_test: stw_test.c ../source/stw.c
	$(CC) $(CFLAGS) -pthread $^ -o $@

worker_test: worker_test.c ../source/worker.c
	$(CC) $(CFLAGS) -pthread $^ -o $@

overlay_test: overlay_test.c ../source/performance.c ../source/console_overlay.c
	$(CC) $(CFLAGS) $^ -lm -o $@

overlay_debug_test: overlay_test.c ../source/performance.c ../source/console_overlay.c ../source/debug_heartbeat.c
	$(CC) $(CFLAGS) -DDND_DEBUG_TELEMETRY $^ -lm -o $@

network_link_test: network_link_test.c ../source/network_link_gamecube.c
	$(CC) $(CFLAGS) -DGEKKO -DHW_DOL -Inetwork_link_sdk $^ -o $@

debug_heartbeat_test: debug_heartbeat_test.c ../source/debug_heartbeat.c ../source/performance.c
	$(CC) $(CFLAGS) -DDND_DEBUG_TELEMETRY $^ -o $@

clean:
	rm -f file_extents_test contiguous_write_test dol_test
	rm -f managed_runtime_tests fst_test card_transaction_test aram_test overlay_test overlay_debug_test debug_heartbeat_test network_link_test worker_test stw_test input_native_test input_overlay_test input_gamecube_test hardware_gamecube_test disc_mount_test
