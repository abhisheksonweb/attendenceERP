"""Download the YuNet + SFace ONNX models into the models/ folder.

Run once after cloning:  python download_models.py
"""
import urllib.request

import config

MODELS = {
    config.YUNET_PATH: (
        "https://github.com/opencv/opencv_zoo/raw/main/models/"
        "face_detection_yunet/face_detection_yunet_2023mar.onnx"
    ),
    config.SFACE_PATH: (
        "https://github.com/opencv/opencv_zoo/raw/main/models/"
        "face_recognition_sface/face_recognition_sface_2021dec.onnx"
    ),
}


def main() -> None:
    config.MODELS_DIR.mkdir(parents=True, exist_ok=True)
    for path, url in MODELS.items():
        if path.exists():
            print(f"exists: {path.name}")
            continue
        print(f"downloading {path.name} ...")
        urllib.request.urlretrieve(url, path)
        print(f"  saved {path.stat().st_size:,} bytes")
    print("done.")


if __name__ == "__main__":
    main()
