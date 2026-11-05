ifeq ($(strip $(DEVKITPPC)),)
$(error "Please set DEVKITPPC in your environment")
endif
ifeq ($(strip $(DEVKITPRO)),)
$(error "Please set DEVKITPRO in your environment")
endif
include $(DEVKITPRO)/libogc2/gamecube_rules
.DELETE_ON_ERROR:
PYTHON ?= python3
TARGET := DolphinDotNet
BUILD := build
SOURCES := source generated
INCLUDES := include
PORTLIBS := $(DEVKITPRO)/libogc2/gamecube
LIBS := -lfat -ldvm -liso9660 -lbba -laesnd -logc -lm
LIBOGC ?= $(DEVKITPRO)/libogc2/gamecube
LIBDIRS := $(PORTLIBS) $(LIBOGC)
LIBPATHS_EXTRA := -L$(CURDIR)/build/exi-overlay -L$(LIBOGC)/lib
CFLAGS := -DDND_GAMECUBE_STORAGE -g -O2 -fstack-usage -Wall -Wextra $(MACHDEP) $(INCLUDE)
# Explicitly opt in for standalone native debug builds; release behavior is unchanged.
DND_MANAGED_HEAP_BYTES ?= 262144
CFLAGS += -DDND_MANAGED_HEAP_BYTES=$(DND_MANAGED_HEAP_BYTES)
CXXFLAGS := $(CFLAGS)
LDFLAGS := -g $(MACHDEP) -Wl,-Map,$(notdir $@).map
ifeq ($(DND_DEBUG_AUTOMATION),1)
CFLAGS += -DDND_DEBUG_AUTOMATION
CXXFLAGS += -DDND_DEBUG_AUTOMATION
LDFLAGS += -Wl,--undefined=dnd_automation_mailbox -Wl,--undefined=dnd_automation_frame_boundary -Wl,--undefined=dnd_asset_corpus_report -Wl,--undefined=dnd_jsystem_fixture
endif
ifneq ($(BUILD),$(notdir $(CURDIR)))
.DEFAULT_GOAL := all
export DOL_SECTIONS := $(CURDIR)/scripts/dol_sections.py
export OUTPUT := $(CURDIR)/$(TARGET)
export VPATH := $(foreach dir,$(SOURCES),$(CURDIR)/$(dir))
export DEPSDIR := $(CURDIR)/$(BUILD)
CFILES := $(foreach dir,$(SOURCES),$(notdir $(wildcard $(dir)/*.c)))
export OFILES := $(CFILES:.c=.o)
export INCLUDE := $(foreach dir,$(INCLUDES),-I$(CURDIR)/$(dir)) $(foreach dir,$(LIBDIRS),-I$(dir)/include) -I$(CURDIR)/$(BUILD)
export LIBPATHS := $(LIBPATHS_EXTRA) $(foreach dir,$(LIBDIRS),-L$(dir)/lib)
.PHONY: all clean
export EXI_OVERLAY := $(CURDIR)/build/exi-overlay/libogc.a
$(EXI_OVERLAY): scripts/build_exi_overlay.sh scripts/verify_exi_source.py scripts/verify_video_source.py vendor/libogc2/exi.c vendor/libogc2/video.c
	@bash scripts/build_exi_overlay.sh
all: $(EXI_OVERLAY) $(BUILD)
	@$(MAKE) --no-print-directory -C $(BUILD) -f $(CURDIR)/Makefile
$(BUILD):
	@mkdir -p $@
clean:
	@rm -fr $(BUILD) $(OUTPUT).elf $(OUTPUT).dol
else
DEPENDS := $(OFILES:.o=.d)
$(OUTPUT).dol: $(OUTPUT).elf $(DOL_SECTIONS)
	elf2dol "$<" "$@"
	$(PYTHON) "$(DOL_SECTIONS)" --pad "$@"
$(OUTPUT).elf: $(OFILES) $(EXI_OVERLAY)
	$(CC) $(OFILES) $(LDFLAGS) $(LIBPATHS) $(LIBS) -o $@
-include $(DEPENDS)
endif
