# LoopDesk

Windows 10·11 64비트용 마우스·키보드 매크로 편집기입니다. 화면에서 동작을 녹화하거나, 108키 입력 선택창에서 키·마우스 동작을 직접 추가할 수 있습니다.

- [Windows 실행 파일 다운로드](https://github.com/glasscola/LoopDesk/raw/v1.0.0/downloads/LoopDesk.exe)
- [화면 캡처가 포함된 PDF 사용 설명서](docs/LoopDesk-사용설명서.pdf)

## 처음 사용하기

1. LoopDesk.exe를 엽니다. 이전 버전이 실행 중이면 작업 표시줄 알림 영역에서 **완전히 종료**합니다.
2. 대상 앱에서 **F6**으로 녹화하고 **F7**로 종료합니다. 저장이 끝나면 편집 창이 열립니다.
3. 동작을 더블클릭하고 **동작 반복 횟수**를 정합니다. Q 입력을 10회로 저장하면 해당 동작만 10번 실행합니다.
4. 대상 창에서 **F9**로 실행합니다. **F10**은 일시정지·재개, **F11**은 실행 정지입니다.

![LoopDesk 동작 목록과 구간 편집](docs/images/editor.png)

## 소스에서 빌드하기

Windows PowerShell에서 `./src/Build.ps1`을 실행하면 `outputs/LoopDesk.exe`가 생성됩니다. 빌드 스크립트는 Windows의 .NET Framework 컴파일러와 Windows 메타데이터를 사용합니다.
