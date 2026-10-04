#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
: "${IMAGE_TAG:?}"
mkdir -p artifacts
# Mikrus 2.1 is x86_64; the image is loaded there as kbb-web:<commit>.
docker build --platform linux/amd64 -t "kbb-web:$IMAGE_TAG" .
docker save "kbb-web:$IMAGE_TAG" | gzip > artifacts/image.tar.gz
du -h artifacts/image.tar.gz
