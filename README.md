# MediaArchiverGUI

사진 및 동영상 파일의 메타데이터(Exif)를 추출하여 연월 및 촬영 기기별로 자동 분류하는 윈도우(WPF) 데스크톱 애플리케이션입니다.

## 📌 주요 기능
* **메타데이터 기반 분류:** `ExifTool` 및 `MetadataExtractor`를 사용하여 미디어 파일의 실제 촬영일시와 기기 모델명을 추출합니다.
* **자동 네이밍 및 폴더링:** `YYYYMMDD_HHMMSS_DeviceModel.ext` 형식으로 파일명을 변경하고 `YYYY년/MM월` 폴더로 자동 이동/복사합니다.
* **동영상 KST 보정:** UTC 기준으로 저장되는 MP4 등의 동영상 촬영 시간을 한국 시간(+9시간)으로 자동 보정합니다. (로컬 시간으로 저장되는 .mts 등은 예외 처리)
* **제조사명 필터링:** 메타데이터에 포함된 불필요한 제조사명(Samsung, Apple 등)을 정규식으로 제거하고 직관적인 모델명(예: GalaxyS22Ultra)으로 맵핑합니다.

## 🚀 실행 방법
1. [Releases](../../releases) 탭에서 최신 버전을 다운로드합니다.
2. 실행 파일(`MediaArchiverGUI.exe`)과 같은 디렉토리에 `tools/exiftool.exe` 파일이 존재해야 동영상 분석이 정상적으로 동작합니다.
3. 프로그램을 실행하고 '원본 폴더'와 '대상 폴더'를 지정한 뒤 분류를 시작합니다.

## 🛠️ 기술 스택
* C# 12, .NET 10.0 (WPF / MVVM)
* [ExifTool](https://exiftool.org/) (동영상 및 복합 메타데이터 추출)
* [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) (이미지 메타데이터 백업 추출기)