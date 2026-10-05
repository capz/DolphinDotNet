ifeq ($(strip $(DEVKITPPC)),)
$(error "Please set DEVKITPPC in your environment")
endif
ifeq ($(strip $(DEVKITPRO)),)
$(error "Please set DEVKITPRO in your environment")
endif
include $(DEVKITPPC)/gamecube_rules
TARGET := DolphinDotNet
BUILD := build
SOURCES := source generated
INCLUDES := include
PORTLIBS := $(DEVKITPRO)/portlibs/gamecube
LIBS := -lopengx -logc -lm
LIBOGC ?= $(DEVKITPRO)/libogc
LIBDIRS := $(PORTLIBS) $(LIBOGC)
LIBPATHS_EXTRA := -L$(LIBOGC)/lib/cube
CFLAGS := -g -O2 -Wall -Wextra $(MACHDEP) $(INCLUDE)
CXXFLAGS := $(CFLAGS)
LDFLAGS := -g $(MACHDEP) -Wl,-Map,$(notdir $@).map
ifneq ($(BUILD),$(notdir $(CURDIR)))
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
$(OUTPUT).dol: $(OUTPUT).elf
$(OUTPUT).elf: $(OFILES)
	$(CC) $^ $(LDFLAGS) $(LIBPATHS) $(LIBS) -o $@
-include $(DEPENDS)
endif
