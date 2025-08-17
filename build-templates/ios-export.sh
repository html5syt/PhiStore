#!/bin/bash

# GitHub Actions 专用脚本 - 生成未签名 IPA
set -eo pipefail

# 配置路径 (基于 GitHub Workspace)
PROJECT_DIR="${GITHUB_WORKSPACE}/builds/ios"
OUTPUT_IPA="${GITHUB_WORKSPACE}/builds/ios/output/PhiStore.ipa"
DERIVED_DATA_PATH="${PROJECT_DIR}/build_temp"

# 自动获取项目文件
XCODEPROJ=$(ls "${PROJECT_DIR}" | grep .xcodeproj)
if [ -z "$XCODEPROJ" ]; then
  echo "❌ 错误: 在 ${PROJECT_DIR} 中未找到 .xcodeproj 文件"
  exit 1
fi

# 自动获取 Scheme 名称
SCHEME_NAME=$(xcodebuild -list -project "${PROJECT_DIR}/${XCODEPROJ}" | awk '/Schemes:/ {getline; print}' | head -1)
if [ -z "$SCHEME_NAME" ]; then
  echo "❌ 错误: 无法获取 Scheme 名称"
  exit 1
fi

echo "ℹ️ 使用项目: ${XCODEPROJ}"
echo "ℹ️ 使用方案: ${SCHEME_NAME}"

# 清理旧文件
rm -rf "${DERIVED_DATA_PATH}" "${OUTPUT_IPA}" "${PROJECT_DIR}/Payload"

# 构建未签名的 .app
echo "🚀 开始构建应用..."
xcodebuild build \
  -project "${PROJECT_DIR}/${XCODEPROJ}" \
  -configuration Release \
  -sdk iphoneos \
  CODE_SIGN_IDENTITY="" \
  CODE_SIGNING_REQUIRED=NO \
  CODE_SIGNING_ALLOWED=NO \
  -derivedDataPath "${DERIVED_DATA_PATH}"

# 查找生成的 .app 文件
APP_PATH=$(find "${DERIVED_DATA_PATH}/Build/Products/Release-iphoneos" -name "*.app" | head -1)
if [ -z "$APP_PATH" ]; then
  echo "❌ 错误: 未找到生成的 .app 文件"
  exit 1
fi

echo "✅ 应用构建成功: $(basename ${APP_PATH})"

# 创建 Payload 并打包 IPA
echo "📦 打包 IPA..."
mkdir -p "${PROJECT_DIR}/Payload"
cp -r "${APP_PATH}" "${PROJECT_DIR}/Payload/"
(cd "${PROJECT_DIR}" && zip -qr "${OUTPUT_IPA}" Payload)

# 清理临时文件
rm -rf "${DERIVED_DATA_PATH}" "${PROJECT_DIR}/Payload"

echo "======================================="
echo "🎉 未签名 IPA 生成成功!"
echo "📁 路径: ${OUTPUT_IPA}"
echo "======================================="