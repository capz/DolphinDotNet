"""Execute display configuration, aspect and logical-canvas APIs through AOT/GC stress."""
from ci_graphics_phase_one import main
if __name__=='__main__':main('DisplayConfigurationSmoke','display_configuration_host.c',('-DDND_DISPLAY_TEST',))
