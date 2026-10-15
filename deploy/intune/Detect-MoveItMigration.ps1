# Intune Win32 app detection script.
# "Detected" = the migration completed: exit 0 and write a line to stdout.
# Not detected = exit 1 with no output, so Intune runs the install command.
$marker = Join-Path $env:ProgramData 'MoveIT\migration.done'
if (Test-Path $marker) {
    Write-Output "MoveIT migration completed."
    exit 0
}
exit 1
