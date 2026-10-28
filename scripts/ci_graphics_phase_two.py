"""Execute phase-two geometry/layout/capture APIs under actual AOT and GC stress."""
from ci_graphics_phase_one import main
if __name__=='__main__':main('GraphicsPhaseTwoSmoke','graphics_phase_two_host.c')
