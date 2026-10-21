# Envia Win+<tecla> (D = área de trabalho, A = configurações rápidas, N = notificações) ou Esc.
param([string]$Tecla = 'D', [switch]$Esc)
Add-Type -Namespace K -Name W -MemberDefinition '[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);'
if ($Esc) {
    [K.W]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero); [K.W]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero)
    return
}
$vk = [byte][char]$Tecla.ToUpperInvariant()
[K.W]::keybd_event(0x5B, 0, 0, [UIntPtr]::Zero)
[K.W]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
[K.W]::keybd_event($vk, 0, 2, [UIntPtr]::Zero)
[K.W]::keybd_event(0x5B, 0, 2, [UIntPtr]::Zero)
