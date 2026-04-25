# onejs debugger 개발 문서

## 개요

onejs v3에 vscode로 디버깅 할 수있는 기능을 만들어줘

* onejs 코드는 건드리지 말아줘
* 참고만하고 소스는 사용하지 않은
* puerts에 필요한 소스가 있다면 OnejsDebugger 복사해서 새로 만듦

## 기능

- typescript에 바로 디버깅이 되도록 기능 추가
- breakpoint에 조건을 추가해서 조건에 맞으면 브레이크가 걸리는 기능 추가
- unity에서 play하면 바로 접속 가능하게 debugging.md 참조해서 puerts의 JsEnv같은 기능 추가
- Quickjs-Debugger macos용 빌드가 없으니 추가
- 결과물들을 패키징해서 UnityPackage(upm 사용) 제작
- Unity에서 기존 OneJS 플러그인들을 내가 만든 플러그인으로 교체하거나 롤백하는 에티터 유틸리티 제작

## 빌드 방법

- OneJS/Auxiliary~/quickjs 폴더를 삭제하고 quickjs폴더를 OneJS/Auxiliary~/로 이동한다.
- 기존 OneJS/Plugins들은 삭제한다.
- build-android.sh, build-ios.sh, build-linux.sh, build-windows.sh, build.sh를 실행한다. (통합 스크립트 개발)
- Quickjs-Debugger windows, linux, macos용으로 빌드한다.
- 위 두가지 결과물은 각각 압축해서 Packages폴더에 복사한다.
- 깃허브 액션으로 OneJS/Plugins, Quickjs-Debugger, Unity용 유틸리티 소스 패키징해서 깃허브 릴리즈에 추가한다.
