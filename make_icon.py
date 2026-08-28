"""Build VDH.ico from the desktop monitor PNG with a 'helper' badge."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

SRC = Path(r"C:\Users\dwgx1\OneDrive\Desktop\e0881d_667d7d32eadb45a282f375385db9a448~mv2.png")
OUT = Path(__file__).resolve().parent / "VDH.ico"
PNG = Path(__file__).resolve().parent / "VDH.png"


def font(size: int) -> ImageFont.FreeTypeFont:
    for p in (
        r"C:\Windows\Fonts\segoeui.ttf",
        r"C:\Windows\Fonts\tahomabd.ttf",
        r"C:\Windows\Fonts\arial.ttf",
    ):
        if Path(p).exists():
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def badge(base: Image.Image) -> Image.Image:
    im = base.convert("RGBA")
    w, h = im.size
    # keep chunky pixels
    canvas = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    body = im.resize((256, 256), Image.Resampling.NEAREST)
    canvas.alpha_composite(body)
    d = ImageDraw.Draw(canvas)
    label = "helper"
    f = font(28)
    bbox = d.textbbox((0, 0), label, font=f)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    pad_x, pad_y = 10, 5
    bw, bh = tw + pad_x * 2, th + pad_y * 2
    x, y = 256 - bw - 8, 256 - bh - 8
    d.rounded_rectangle((x, y, x + bw, y + bh), radius=8, fill=(11, 18, 32, 235), outline=(91, 140, 255, 255), width=2)
    d.text((x + pad_x, y + pad_y - 2), label, font=f, fill=(238, 241, 248, 255))
    return canvas


def to_ico(img: Image.Image, path: Path) -> None:
    sizes = [(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    frames = [img.resize(s, Image.Resampling.LANCZOS) for s in sizes]
    frames[0].save(path, format="ICO", sizes=[f.size for f in frames], append_images=frames[1:])


def main() -> None:
    img = badge(Image.open(SRC))
    img.save(PNG)
    to_ico(img, OUT)
    print("wrote", OUT, OUT.stat().st_size, "png", PNG)


if __name__ == "__main__":
    main()
