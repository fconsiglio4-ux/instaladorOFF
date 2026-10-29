$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$exe = Join-Path $PSScriptRoot 'Maquim-ComputerSync.exe'
$escape = [Security.SecurityElement]::Escape($exe)
$start = (Get-Date).AddMinutes(1).ToString('yyyy-MM-ddTHH:mm:ss')
$xml = @"
<Task version="1.3" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><Description>Reenvio exclusivo da fila de computadores Maquim.</Description></RegistrationInfo>
  <Triggers>
    <LogonTrigger><Enabled>true</Enabled><UserId>$sid</UserId><Delay>PT30S</Delay></LogonTrigger>
    <TimeTrigger><Repetition><Interval>PT15M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition><StartBoundary>$start</StartBoundary><Enabled>true</Enabled></TimeTrigger>
  </Triggers>
  <Principals><Principal id="User"><UserId>$sid</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable><AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled><ExecutionTimeLimit>PT14M</ExecutionTimeLimit>
  </Settings>
  <Actions Context="User"><Exec><Command>$escape</Command></Exec></Actions>
</Task>
"@
Register-ScheduledTask -TaskName ('Maquim-Computadores-' + $sid) -Xml $xml -Force | Out-Null
