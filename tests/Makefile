CC ?= cc
ifeq ($(shell uname -s),Darwin)
GC_LDFLAGS = -Wl,-dead_strip
else
GC_LDFLAGS = -Wl,--gc-sections
endif

CFLAGS ?= -std=c11 -Wall -Wextra -Werror -I../include

.PHONY: test clean

test: ipl_font_test
test: defrag_ownership_test defrag_planner_test verified_copy_test evacuation_journal_test evacuation_test
test: defrag_batch_test defrag_rebuild_test
test: raw_move_codec_test
test: raw_move_test

raw_move_test: raw_move_test.c ../source/raw_move.c ../source/raw_move_fs.c ../source/raw_move_codec.c ../source/defrag_allocation.c ../source/verified_copy.c
	$(CC) $(CFLAGS) $^ -o $@

raw_move_codec_test: raw_move_codec_test.c ../source/raw_move_codec.c ../source/verified_copy.c
	$(CC) $(CFLAGS) $^ -o $@

defrag_rebuild_test: defrag_rebuild_test.c ../source/defrag_rebuild.c
	$(CC) $(CFLAGS) $^ -o $@

defrag_batch_test: defrag_batch_test.c ../source/defrag_batch.c ../source/evacuation.c ../source/evacuation_journal.c ../source/verified_copy.c
	$(CC) $(CFLAGS) $^ -o $@

evacuation_journal_test: evacuation_journal_test.c ../source/evacuation_journal.c ../source/verified_copy.c
	$(CC) $(CFLAGS) $^ -o $@

evacuation_test: evacuation_test.c ../source/evacuation.c ../source/evacuation_journal.c ../source/verified_copy.c
	$(CC) $(CFLAGS) $^ -o $@

verified_copy_test: verified_copy_test.c ../source/verified_copy.c
	$(CC) $(CFLAGS) $^ -o $@

defrag_ownership_test: defrag_ownership_test.c ../source/defrag_allocation.c
	$(CC) $(CFLAGS) $^ -o $@

defrag_planner_test: defrag_planner_test.c ../source/defrag_planner.c
	$(CC) $(CFLAGS) $^ -o $@

test: card_manager_test mesh_streams_test display_mode_test graphics_resource_boundaries_test graphics_phase_two_test graphics_submission_test render_state_gx_test overlay_list_test array_fast_test indexed_mesh_gx_test canvas_rectangle_test texture_reuse_test throughput_runtime_test asset_native_test mesh_range_test native_buffer_crc_test file_optimize_test file_extents_test defrag_allocation_test contiguous_write_test managed_runtime_tests fst_test card_transaction_test aram_test overlay_test overlay_debug_test debug_heartbeat_test network_link_test worker_test stw_test input_native_test debug_automation_test asset_corpus_abi_test jsystem_fixture_telemetry_test asset_compat_test input_overlay_test input_gamecube_test hardware_gamecube_test disc_mount_test dol_test
	./card_manager_test
	./mesh_streams_test
	./display_mode_test
	./ipl_font_test
	./graphics_resource_boundaries_test
	./graphics_phase_two_test
	./graphics_submission_test
	./render_state_gx_test
	./overlay_list_test
	./array_fast_test
	./indexed_mesh_gx_test
	./canvas_rectangle_test
	./texture_reuse_test
	./throughput_runtime_test
	./asset_native_test
	./mesh_range_test
	./native_buffer_crc_test
	./file_optimize_test
	./file_extents_test
	./defrag_allocation_test
	./defrag_ownership_test
	./defrag_planner_test
	./verified_copy_test
	./evacuation_journal_test
	./evacuation_test
	./defrag_batch_test
	./defrag_rebuild_test
	./raw_move_codec_test
	./raw_move_test
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
	./debug_automation_test
	./asset_corpus_abi_test
	./jsystem_fixture_telemetry_test
	./asset_compat_test
	./input_overlay_test
	./input_gamecube_test
	./hardware_gamecube_test
	./disc_mount_test
	./dol_test

ipl_font_test: ipl_font_test.c ../source/ipl_font.c ../include/dnd_ipl_font.h ipl_font_stub/ogc/system.h
	$(CC) $(CFLAGS) -DDND_IPL_FONT_TEST -Iipl_font_stub ipl_font_test.c ../source/ipl_font.c -o $@

file_optimize_test: file_optimize_test.c ../source/file_optimize.c ../source/verified_copy.c
	$(CC) $(CFLAGS) $^ -o $@

file_extents_test: file_extents_test.c ../source/file_extents.c
	$(CC) $(CFLAGS) $^ -o $@

defrag_allocation_test: defrag_allocation_test.c ../source/defrag_allocation.c
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

asset_corpus_abi_test: asset_corpus_abi_test.c ../source/asset_corpus.c
	$(CC) $(CFLAGS) -DDND_DEBUG_AUTOMATION $^ -o $@

jsystem_fixture_telemetry_test: jsystem_fixture_telemetry_test.c ../source/asset_fixture.c
	$(CC) $(CFLAGS) -DDND_DEBUG_AUTOMATION $^ -o $@

asset_compat_test: asset_compat_test.c ../source/asset_compat.c
	$(CC) $(CFLAGS) -DDND_DEBUG_AUTOMATION $^ -o $@

debug_automation_test: debug_automation_test.c input_test_backend.c ../source/input.c ../source/debug_automation.c
	$(CC) $(CFLAGS) -DDND_DEBUG_AUTOMATION $^ -lm -o $@

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

ifeq ($(shell uname -s),Darwin)
CRC_DEAD_STRIP = -Wl,-dead_strip
else
CRC_DEAD_STRIP = -Wl,--gc-sections
endif
native_buffer_crc_test: native_buffer_crc_test.c ../source/draw_resources.c
	$(CC) $(CFLAGS) -ffunction-sections -fdata-sections $^ $(CRC_DEAD_STRIP) -lm -o $@
mesh_range_test: mesh_range_test.c ../source/draw_resources.c
	$(CC) $(CFLAGS) -ffunction-sections -fdata-sections $^ $(CRC_DEAD_STRIP) -lm -o $@
canvas_rectangle_test: canvas_rectangle_test.c ../source/draw_resources.c
	$(CC) $(CFLAGS) -ffunction-sections -fdata-sections $^ $(CRC_DEAD_STRIP) -lm -o $@

texture_reuse_test: texture_reuse_test.c ../source/draw_resources.c
	$(CC) $(CFLAGS) -ffunction-sections -fdata-sections $^ $(CRC_DEAD_STRIP) -lm -o $@

clean:
	rm -f raw_move_codec_test
	rm -f raw_move_test
	rm -f defrag_ownership_test defrag_planner_test verified_copy_test
	rm -f evacuation_journal_test evacuation_test
	rm -f defrag_batch_test
	rm -f defrag_rebuild_test
	rm -f card_manager_test
	rm -f ipl_font_test
	rm -f display_mode_test
	rm -f graphics_resource_boundaries_test
	rm -f graphics_phase_two_test graphics_submission_test render_state_gx_test canvas_rectangle_test indexed_mesh_gx_test array_fast_test overlay_list_test
	rm -f texture_reuse_test
	rm -f asset_native_test throughput_runtime_test
	rm -f mesh_streams_test mesh_range_test native_buffer_crc_test
	rm -f file_optimize_test file_extents_test defrag_allocation_test contiguous_write_test dol_test
	rm -f managed_runtime_tests fst_test card_transaction_test aram_test overlay_test overlay_debug_test debug_heartbeat_test network_link_test worker_test stw_test input_native_test debug_automation_test asset_corpus_abi_test jsystem_fixture_telemetry_test asset_compat_test input_overlay_test input_gamecube_test hardware_gamecube_test disc_mount_test

asset_native_test: asset_native_test.c ../source/storage.c ../source/draw_resources.c ../source/managed_runtime.c ../source/stw.c ../source/gc_stw.c ../source/file_optimize.c ../source/file_extents.c ../source/contiguous_write.c ../source/verified_copy.c
	$(CC) $(CFLAGS) -Dread=dnd_asset_test_read -pthread -ffunction-sections -fdata-sections $^ $(GC_LDFLAGS) -lm -o $@

throughput_runtime_test: throughput_runtime_test.c ../source/async_runtime.c ../source/managed_runtime.c ../source/stw.c ../source/gc_stw.c
	$(CC) $(CFLAGS) -pthread -ffunction-sections -fdata-sections $^ $(GC_LDFLAGS) -lm -o $@

indexed_mesh_gx_test: indexed_mesh_gx_test.c ../include/dnd_indexed_mesh.inc
	$(CC) $(CFLAGS) indexed_mesh_gx_test.c -o $@

array_fast_test: array_fast_test.c ../source/managed_runtime.c ../source/stw.c ../source/gc_stw.c
	$(CC) $(CFLAGS) -pthread $^ -o $@

overlay_list_test: graphics_phase_two_test graphics_submission_test render_state_gx_test overlay_list_test.c ../include/dnd_overlay_list.h
	$(CC) $(CFLAGS) overlay_list_test.c -o $@

render_state_gx_test: graphics_phase_two_test graphics_submission_test render_state_gx_test.c ../include/dnd_render_gx.inc ../include/dnd_render_state.h
	$(CC) $(CFLAGS) render_state_gx_test.c -lm -o $@

graphics_submission_test: graphics_phase_two_test graphics_submission_test.c ../source/draw_resources.c
	$(CC) $(CFLAGS) -ffunction-sections -fdata-sections graphics_submission_test.c ../source/draw_resources.c $(GC_LDFLAGS) -lm -o $@

graphics_phase_two_test: graphics_phase_two_test.c ../source/draw_resources.c
	$(CC) $(CFLAGS) -ffunction-sections -fdata-sections $^ $(GC_LDFLAGS) -lm -o $@

graphics_resource_boundaries_test: graphics_resource_boundaries_test.c ../source/draw_resources.c
	$(CC) $(CFLAGS) -ffunction-sections -fdata-sections $^ $(GC_LDFLAGS) -lm -o $@

display_mode_test: mesh_streams_test display_mode_test.c ../include/dnd_display_gx.inc
	$(CC) $(CFLAGS) display_mode_test.c -o $@

.PHONY: system_firmware_native_test
system_firmware_native_test:
	$(CC) $(CFLAGS) -DDND_FIRMWARE_TEST system_firmware_native_test.c ../source/system_firmware.c -o /tmp/dnd-system-firmware-native-test
	/tmp/dnd-system-firmware-native-test
test: system_firmware_native_test

mesh_streams_test: mesh_streams_test.c ../source/draw_resources.c ../include/dnd_mesh_streams.inc
	$(CC) $(CFLAGS) -ffunction-sections -fdata-sections mesh_streams_test.c ../source/draw_resources.c $(GC_LDFLAGS) -lm -o $@

card_manager_test: card_manager_test.c ../source/card_manager.c
	$(CC) $(CFLAGS) -DDND_GAMECUBE_STORAGE -Icard_sdk $^ -o $@
