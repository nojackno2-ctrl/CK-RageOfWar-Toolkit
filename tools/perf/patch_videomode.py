"""
Patch SetVideoMode in 'Celtic kings.exe' to prevent crash on modern Windows 10/11 at 2K/4K/custom resolutions.

Offset 0x002BE340 (VA 0x006BE340) in 'Celtic kings.exe':
Original bytes:
  81 EC 38 01 00 00  (sub esp, 0x138)
  53                 (push ebx)
  56                 (push esi)
  57                 (push edi)
  33 DB              (xor ebx, ebx)
  ...
  call ChangeDisplaySettingsA with dmBitsPerPel = 16

Patched bytes:
  31 C0              (xor eax, eax - return 0 / SUCCESS)
  C3                 (ret)
  90 90 90           (nop)
"""

import sys
import os
import argparse
import shutil

OFFSET_SETVIDEOMODE = 0x002BE340
ORIG_BYTES = b'\x81\xec\x38\x01\x00\x00'
PATCH_BYTES = b'\x31\xc0\xc3\x90\x90\x90'

DEFAULT_GAME_DIR = r"C:\Program Files (x86)\Steam\steamapps\common\CK_RageOfWar"

def get_paths(game_dir):
    exe_path = os.path.join(game_dir, "Celtic kings.exe")
    backup_dir = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "backup")
    backup_path = os.path.join(backup_dir, "Celtic kings.exe.orig")
    return exe_path, backup_dir, backup_path

def ensure_backup(exe_path, backup_dir, backup_path):
    if not os.path.exists(backup_path):
        os.makedirs(backup_dir, exist_ok=True)
        if not os.path.exists(exe_path):
            print(f"Error: Game executable not found at {exe_path}", file=sys.stderr)
            return False
        shutil.copy2(exe_path, backup_path)
        print(f"Created pristine backup at {backup_path}")
    return True

def is_patched(data):
    return data[OFFSET_SETVIDEOMODE:OFFSET_SETVIDEOMODE+3] == b'\x31\xc0\xc3'

def is_orig(data):
    return data[OFFSET_SETVIDEOMODE:OFFSET_SETVIDEOMODE+6] == ORIG_BYTES

def apply_patch(game_dir):
    exe_path, backup_dir, backup_path = get_paths(game_dir)
    if not ensure_backup(exe_path, backup_dir, backup_path):
        return False

    with open(exe_path, "rb") as f:
        data = bytearray(f.read())

    if is_patched(data):
        print("Video mode patch is already applied to Celtic kings.exe.")
        return True

    if not is_orig(data):
        print("Warning: Bytes at SetVideoMode offset did not match expected original bytes.")
        print(f"Found: {data[OFFSET_SETVIDEOMODE:OFFSET_SETVIDEOMODE+6].hex()}")

    data[OFFSET_SETVIDEOMODE:OFFSET_SETVIDEOMODE+len(PATCH_BYTES)] = PATCH_BYTES

    with open(exe_path, "wb") as f:
        f.write(data)

    print("Successfully applied Video Mode patch (Crash fix on 2K/4K) to Celtic kings.exe.")
    return True

def remove_patch(game_dir):
    exe_path, backup_dir, backup_path = get_paths(game_dir)
    if not os.path.exists(backup_path):
        print("Error: No backup found to restore from.", file=sys.stderr)
        return False

    with open(backup_path, "rb") as f:
        orig_data = f.read()

    with open(exe_path, "wb") as f:
        f.write(orig_data)

    print("Restored Celtic kings.exe from pristine backup.")
    return True

def status(game_dir):
    exe_path, _, _ = get_paths(game_dir)
    if not os.path.exists(exe_path):
        print(f"Game exe not found: {exe_path}")
        return

    with open(exe_path, "rb") as f:
        data = f.read()

    if is_patched(data):
        print("SetVideoMode Status: PATCHED (Modern 16bpp Display Crash Fix Active)")
    elif is_orig(data):
        print("SetVideoMode Status: ORIGINAL (Unpatched - may crash at 2K/4K/custom resolutions)")
    else:
        print("SetVideoMode Status: UNKNOWN / MODIFIED")

def main():
    parser = argparse.ArgumentParser(description="Patch Celtic kings.exe SetVideoMode to prevent 2K resolution crash")
    parser.add_argument("--game-dir", default=DEFAULT_GAME_DIR, help="Path to CK_RageOfWar directory")
    parser.add_argument("--apply", action="store_true", help="Apply video mode crash patch")
    parser.add_argument("--restore", action="store_true", help="Restore original SetVideoMode")
    parser.add_argument("--status", action="store_true", help="Check patch status")

    args = parser.parse_args()

    if args.restore:
        remove_patch(args.game_dir)
    elif args.status:
        status(args.game_dir)
    else:
        apply_patch(args.game_dir)

if __name__ == "__main__":
    main()
