# PlatformIO post script: after every build, merge bootloader + partitions + boot_app0 + app into
# .pio/build/<env>/<env>-factory.bin — the image the app's USB init writes at offset 0x0.
# CI renames it to <DEVICE_NAME>-factory.bin; debug builds of the app bundle it under the env name.
Import("env")


def merge_factory(source, target, env):
    out = env.subst("$BUILD_DIR/${PIOENV}-factory.bin")
    images = []
    for offset, path in env.get("FLASH_EXTRA_IMAGES", []):
        images += [offset, '"%s"' % env.subst(path)]
    env.Execute(" ".join([
        '"$PYTHONEXE" "$OBJCOPY"',
        "--chip", env.BoardConfig().get("build.mcu"),
        "merge_bin", "-o", '"%s"' % out,
        *images,
        env.subst("$ESP32_APP_OFFSET") or "0x10000", '"%s"' % target[0].get_abspath(),
    ]))


env.AddPostAction("$BUILD_DIR/${PROGNAME}.bin", merge_factory)
