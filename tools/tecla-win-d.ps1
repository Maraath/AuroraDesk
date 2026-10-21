# Envia Win+D ("Mostrar área de trabalho") para testes da camada dos widgets.
Add-Type -Namespace K -Name B -MemberDefinition '[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);'
[K.B]::keybd_event(0x5B, 0, 0, [UIntPtr]::Zero)   # Win pressionada
[K.B]::keybd_event(0x44, 0, 0, [UIntPtr]::Zero)   # D
[K.B]::keybd_event(0x44, 0, 2, [UIntPtr]::Zero)
[K.B]::keybd_event(0x5B, 0, 2, [UIntPtr]::Zero)   # Win solta
