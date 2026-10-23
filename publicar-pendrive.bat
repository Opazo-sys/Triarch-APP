@echo off
chcp 65001 >nul
echo ========================================================
echo   TRIARCH v1.0 - GERADOR DE VERSAO PORTATIL (PENDRIVE)
echo ========================================================
echo.
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ".\dist\Triarch-Portable"
echo.
echo Concluido! Os arquivos portateis estao em: .\dist\Triarch-Portable
pause