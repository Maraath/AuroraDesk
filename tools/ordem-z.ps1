# Mostra a ordem Z (de cima para baixo) das janelas visíveis do Aurora Desk e das janelas da
# área de trabalho (Progman/WorkerW), para conferir a camada "na área de trabalho".
Add-Type -Namespace Z -Name W -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
'@
# Processo principal (o vigia da barra de tarefas tem o mesmo nome, mas roda com --vigia).
$aurora = (Get-CimInstance Win32_Process -Filter "Name='AuroraDesk.exe'" | Where-Object { $_.CommandLine -notmatch '--vigia' } | Select-Object -First 1).ProcessId
$h = [Z.W]::GetTopWindow([IntPtr]::Zero)
$i = 0; $visiveis = 0
$lista = @()
while ($h -ne [IntPtr]::Zero) {
    if ([Z.W]::IsWindowVisible($h)) {
        $visiveis++
        $p = 0; [Z.W]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
        $c = New-Object System.Text.StringBuilder 128; [Z.W]::GetClassName($h, $c, 128) | Out-Null
        $t = New-Object System.Text.StringBuilder 128; [Z.W]::GetWindowText($h, $t, 128) | Out-Null
        $ex = [Z.W]::GetWindowLongPtr($h, -20).ToInt64()
        $lista += [pscustomobject]@{ I = $i; Classe = "$c"; Titulo = "$t"; Nosso = ($p -eq $aurora); Topo = (($ex -band 8) -ne 0); Ferramenta = (($ex -band 0x80) -ne 0) }
        if ($p -eq $aurora -or $c.ToString() -in 'Progman', 'WorkerW') {
            '{0,4} {1,-28} {2,-30} {3}' -f $i, $c, $t, $(if (($ex -band 8) -ne 0) { 'topmost' } else { '' })
        }
    }
    $h = [Z.W]::GetWindow($h, 2); $i++
}
"(total de janelas no nível superior percorridas: $i, visíveis: $visiveis)"

# Veredito: entre o widget mais alto e o desktop só podem existir widgets (nenhuma janela comum).
$widgets = $lista | Where-Object { $_.Nosso -and $_.Titulo -eq 'Aurora Desk - widget' }
$desk = $lista | Where-Object { $_.Classe -eq 'Progman' } | Select-Object -First 1
if ($widgets -and $desk) {
    $alto = ($widgets | Measure-Object I -Minimum).Minimum
    $intrusas = $lista | Where-Object { $_.I -gt $alto -and $_.I -lt $desk.I -and -not $_.Nosso -and -not $_.Topo }
    $abaixo = $widgets | Where-Object { $_.I -gt $desk.I }
    if ($abaixo) { "VEREDITO: FALHA - $(@($abaixo).Count) widget(s) atrás do desktop (invisíveis)" }
    elseif ($intrusas) { "VEREDITO: FALHA - widgets acima de $(@($intrusas).Count) janela(s) comum(ns): $(($intrusas | % { $_.Classe }) -join ', ')" }
    else { 'VEREDITO: OK - widgets logo acima do desktop e abaixo das janelas' }
}
