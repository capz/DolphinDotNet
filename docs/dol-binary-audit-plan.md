# DOL binary audit and remediation plan

## Confirmed defect: data-section size alignment
The October 12 CI DOL had an initialized data section with length 0x159FC, which is 4 bytes short of a 32-byte multiple. This explains Dolphin's loader warning. The current main branch already includes an ELF-to-DOL padding step (`scripts/dol_sections.py --pad`) and post-build validation. Preserve these checks rather than adding a second converter.

## This PR: static format hardening
- Verify each populated section's file offset and size are aligned to 32 bytes, and its load address is aligned to 32 bytes.
- Verify section contents remain within GameCube MEM1 and occupied file and memory regions do not overlap one another.
- Validate the executable entry point lands inside a text section.
- Validate BSS bounds in MEM1, and reject nonzero initialized data overlapping BSS. Zero-filled overlap introduced by safe tail padding is explicitly allowed.
- Exercise these rules in existing CI unit tests, then validate the actual generated DOL during the GameCube build.

## Follow-ups requiring evidence
1. **BSS start alignment:** the previously audited BSS began at 0x8006DC3C. Confirm whether devkitPPC/libogc requires stronger BSS alignment; do not blindly move BSS or rewrite the header, as the startup clear routine and linked references depend on the ELF layout.
2. **Unwind metadata (~50 KB in earlier ELF):** inspect the ELF section flags, program headers and libogc runtime usage. Only remove `.eh_frame` or `.eh_frame_hdr` after confirming no required runtime unwinder references them. Quantify DOL vs ELF impact and test GameCube boot.
3. **Disassembly / bootstrap:** verify the start address and branch target sections using PowerPC objdump/readelf in CI. Static checks cannot replace an actual Dolphin emulator and GameCube boot test.

## Acceptance
CI format tests pass; generated DOL passes the validator; valid payload bytes and linked addresses are preserved; no nonzero BSS overlap. Run Dolphin emulator and physical hardware boot tests separately to establish runtime correctness.

## Second-stage static ELF/DOL verification

`scripts/verify_dol_elf.py` validates 32-bit big-endian PowerPC ELF headers and PT_LOAD bounds, checks ELF and DOL entry points agree, compares each loadable ELF payload byte with the DOL image at its linked load address, and validates ELF zero-fill extents against the DOL BSS region. CI also archives `powerpc-eabi-readelf` program headers and an `objdump -d` disassembly alongside build artifacts. This is static validation; it cannot establish bootability.

Native unwind metadata remains under observation. No unwind sections are removed, because blindly discarding them may break runtime expectations. Future optimization requires cross-toolchain linker provenance and hardware/emulator tests.
