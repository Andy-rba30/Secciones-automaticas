<#
.SYNOPSIS
    Compila ArbaSecciones contra Civil 3D 2027, corre las pruebas, arma el bundle y lo instala.

.DESCRIPTION
    1. Busca Civil 3D 2027 en el registro (carpeta y versión interna, ej. R26.0).
    2. Corre las pruebas de la lógica pura (no necesitan AutoCAD).
    3. Compila el plugin contra las DLL de esa instalación.
    4. Arma artefactos\ArbaSecciones.bundle y artefactos\ArbaSecciones.bundle.zip.
    5. Copia el bundle a %APPDATA%\Autodesk\ApplicationPlugins (salvo con -SinInstalar).

.PARAMETER AcadDir
    Carpeta de instalación, si no se quiere la detectada. Ej.: "C:\Program Files\Autodesk\AutoCAD 2027"

.PARAMETER Serie
    Versión interna para PackageContents.xml, si no se quiere la detectada. Ej.: R26.0

.PARAMETER SinInstalar
    Solo compila y arma el bundle y el zip.
#>
[CmdletBinding()]
param(
    [string]$AcadDir,
    [string]$Serie,
    [switch]$SinInstalar
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$raiz = $PSScriptRoot
$utf8SinBom = New-Object System.Text.UTF8Encoding($false)

function Paso([string]$texto) { Write-Host ''; Write-Host ('== ' + $texto) -ForegroundColor Cyan }
function Fallar([string]$texto) { Write-Host ''; Write-Host ('ERROR: ' + $texto) -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------- .NET 10
Paso 'Comprobando el SDK de .NET 10'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { Fallar 'No se encontró "dotnet". Instala el SDK de .NET 10 (x64).' }
$sdks = & dotnet --list-sdks
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) { Fallar ('Se necesita el SDK de .NET 10. SDK instalados: ' + ($sdks -join ', ')) }
Write-Host ('SDK: ' + (($sdks | Where-Object { $_ -match '^10\.' }) | Select-Object -Last 1))

# ---------------------------------------------------------------- Civil 3D 2027
Paso 'Buscando Civil 3D 2027'
$detectado = $null
$claves = Get-ChildItem 'HKLM:\SOFTWARE\Autodesk\AutoCAD' -ErrorAction SilentlyContinue
foreach ($release in $claves) {
    foreach ($producto in (Get-ChildItem $release.PSPath -ErrorAction SilentlyContinue)) {
        $p = Get-ItemProperty $producto.PSPath -ErrorAction SilentlyContinue
        if ($null -eq $p -or [string]::IsNullOrEmpty($p.AcadLocation)) { continue }
        $nombre = [string]$p.ProductName
        if ($nombre -match '2027' -and (Test-Path (Join-Path $p.AcadLocation 'C3D'))) {
            $detectado = [pscustomobject]@{ Serie = $release.PSChildName; Carpeta = $p.AcadLocation.TrimEnd('\'); Producto = $nombre }
        }
    }
}

if ([string]::IsNullOrEmpty($AcadDir)) {
    if ($null -eq $detectado) { Fallar 'No se encontró Civil 3D 2027 en el registro. Pasa la carpeta con -AcadDir.' }
    $AcadDir = $detectado.Carpeta
}
if (-not (Test-Path $AcadDir)) { Fallar ('No existe la carpeta ' + $AcadDir) }

if ([string]::IsNullOrEmpty($Serie)) {
    if ($null -ne $detectado) { $Serie = $detectado.Serie }
    else { $Serie = 'R26.0'; Write-Host 'Aviso: no se detectó la versión interna; se usa R26.0. Revisa ACADVER en Civil 3D.' -ForegroundColor Yellow }
}
if ($null -ne $detectado) { Write-Host ('Producto: ' + $detectado.Producto) }
Write-Host ('Carpeta:  ' + $AcadDir)
Write-Host ('Serie:    ' + $Serie)

function Buscar-Dll([string]$nombre, [string[]]$subcarpetas) {
    foreach ($s in $subcarpetas) {
        if ([string]::IsNullOrEmpty($s)) { $ruta = Join-Path $AcadDir $nombre }
        else { $ruta = Join-Path (Join-Path $AcadDir $s) $nombre }
        if (Test-Path $ruta) { return $ruta }
    }
    $hallado = Get-ChildItem -Path $AcadDir -Recurse -Filter $nombre -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $hallado) { Fallar ('No se encontró ' + $nombre + ' dentro de ' + $AcadDir) }
    return $hallado.FullName
}

$refs = [ordered]@{
    AcCoreMgdPath  = Buscar-Dll 'accoremgd.dll'  @('')
    AcDbMgdPath    = Buscar-Dll 'acdbmgd.dll'    @('')
    AcMgdPath      = Buscar-Dll 'acmgd.dll'      @('')
    AdWindowsPath  = Buscar-Dll 'AdWindows.dll'  @('')
    AecBaseMgdPath = Buscar-Dll 'AecBaseMgd.dll' @('ACA', '')
    AeccDbMgdPath  = Buscar-Dll 'AeccDbMgd.dll'  @('C3D', '')
}
foreach ($k in $refs.Keys) { Write-Host ('  ' + $k.PadRight(15) + $refs[$k]) }

# ---------------------------------------------------------------- Pruebas
Paso 'Pruebas de la lógica pura (sin AutoCAD)'
& dotnet run --project (Join-Path $raiz 'tests\ArbaSecciones.Core.Tests') -c Release
if ($LASTEXITCODE -ne 0) { Fallar 'Hay pruebas que fallan. No se compila el plugin.' }

# ---------------------------------------------------------------- Compilación
Paso 'Compilando el plugin'
$argumentos = @('build', (Join-Path $raiz 'src\ArbaSecciones.Plugin\ArbaSecciones.Plugin.csproj'), '-c', 'Release', '-nologo', ('-p:AcadDir=' + $AcadDir))
foreach ($k in $refs.Keys) { $argumentos += ('-p:' + $k + '=' + $refs[$k]) }
& dotnet @argumentos
if ($LASTEXITCODE -ne 0) { Fallar 'La compilación falló. Revisa los errores de arriba.' }

$salida = Join-Path $raiz 'src\ArbaSecciones.Plugin\bin\Release\net10.0-windows'
if (-not (Test-Path (Join-Path $salida 'ArbaSecciones.Plugin.dll'))) { Fallar ('No se encontró la DLL compilada en ' + $salida) }

# ---------------------------------------------------------------- Bundle
Paso 'Armando el bundle'
$artefactos = Join-Path $raiz 'artefactos'
$bundle = Join-Path $artefactos 'ArbaSecciones.bundle'
$contenido = Join-Path $bundle 'Contents'
if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
New-Item -ItemType Directory -Path $contenido -Force | Out-Null

foreach ($archivo in @('ArbaSecciones.Plugin.dll', 'ArbaSecciones.Plugin.pdb', 'ArbaSecciones.Core.dll', 'ArbaSecciones.Core.pdb', 'ArbaSecciones.Plugin.deps.json')) {
    $origen = Join-Path $salida $archivo
    if (Test-Path $origen) { Copy-Item $origen $contenido }
}
Copy-Item (Join-Path $raiz 'bundle\LEEME.txt') $contenido

$xml = [System.IO.File]::ReadAllText((Join-Path $raiz 'bundle\PackageContents.xml'), [System.Text.Encoding]::UTF8)
$xml = $xml.Replace('__SERIE__', $Serie)
[System.IO.File]::WriteAllText((Join-Path $bundle 'PackageContents.xml'), $xml, $utf8SinBom)

$zip = Join-Path $artefactos 'ArbaSecciones.bundle.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $bundle -DestinationPath $zip
Write-Host ('Bundle: ' + $bundle)
Write-Host ('Zip:    ' + $zip)

# ---------------------------------------------------------------- Instalación
if ($SinInstalar) { Write-Host ''; Write-Host 'Listo (sin instalar).' -ForegroundColor Green; exit 0 }

Paso 'Instalando'
if (Get-Process -Name 'acad' -ErrorAction SilentlyContinue) {
    Fallar 'Civil 3D está abierto y tiene la DLL en uso. Ciérralo y vuelve a correr el script (o usa -SinInstalar).'
}
$plugins = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
$destino = Join-Path $plugins 'ArbaSecciones.bundle'
if (Test-Path $destino) { Remove-Item $destino -Recurse -Force }
Copy-Item $bundle $plugins -Recurse
Write-Host ('Instalado en ' + $destino)
Write-Host ''
Write-Host 'Listo. Abre Civil 3D 2027: pestaña ARBA > Secciones, o el comando ARBASEC.' -ForegroundColor Green
