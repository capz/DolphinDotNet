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
LIBS := -lopengx -lfat -ldvm -liso9660 -lbba -logc -lm
LIBOGC ?= $(DEVKITPRO)/libogc2/gamecube
LIBDIRS := $(PORTLIBS) $(LIBOGC)
LIBPATHS_EXTRA := -L$(CURDIR)/build/exi-overlay -L$(LIBOGC)/lib
CFLAGS := -DDND_GAMECUBE_STORAGE -g -O2 -fstack-usage -Wall -Wextra $(MACHDEP) $(INCLUDE)
CXXFLAGS := $(CFLAGS)
LDFLAGS := -g $(MACHDEP) -Wl,-Map,$(notdir $@).map
ifneq ($(BUILD),$(notdir $(CURDIR)))
export DOL_SECTIONS := $(CURDIR)/scripts/dol_sections.py
export OUTPUT := $(CURDIR)/$(TARGET)
export VPATH := $(foreach dir,$(SOURCES),$(CURDIR)/$(dir))
export DEPSDIR := $(CURDIR)/$(BUILD)
CFILES := $(foreach dir,$(SOURCES),$(notdir $(wildcard $(dir)/*.c)))
export OFILES := $(CFILES:.c=.o)
export INCLUDE := $(foreach dir,$(INCLUDES),-I$(CURDIR)/$(dir)) $(foreach dir,$(LIBDIRS),-I$(dir)/include) -I$(CURDIR)/$(BUILD)
export LIBPATHS := $(foreach dir,$(LIBDIRS),-L$(dir)/lib) $(LIBPATHS_EXTRA)
.PHONY: all clean
all: $(BUILD)
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
$(OUTPUT).elf: $(OFILES)
	$(CC) $^ $(LDFLAGS) $(LIBPATHS) $(LIBS) -o $@
-include $(DEPENDS)
endif
