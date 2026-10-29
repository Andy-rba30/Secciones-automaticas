@echo off
rem Compila, prueba, arma e instala ArbaSecciones. Argumentos opcionales: -SinInstalar  -AcadDir "carpeta"  -Serie R26.0
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0compilar.ps1" %*
if errorlevel 1 (
  echo.
  echo La compilacion no termino. Revisa el mensaje de arriba.
)
pause
