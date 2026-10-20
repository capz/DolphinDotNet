/* Shared with the recording GX tests. RGB receives lighting; alpha remains
 * vertex/material opacity even for lit materials and normal-mapped reflections. */
static void dnd_material_channels(bool lit) {
    GX_SetChanCtrl(GX_COLOR0,lit?GX_ENABLE:GX_DISABLE,GX_SRC_REG,GX_SRC_VTX,
                  lit?GX_LIGHT0:GX_LIGHTNULL,lit?GX_DF_CLAMP:GX_DF_NONE,GX_AF_NONE);
    GX_SetChanCtrl(GX_ALPHA0,GX_DISABLE,GX_SRC_REG,GX_SRC_VTX,
                  GX_LIGHTNULL,GX_DF_NONE,GX_AF_NONE);
}
