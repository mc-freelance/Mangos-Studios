# Device Identity

Device Identity is an **experimental, work-in-progress** security feature that may change substantially or be removed. It is disabled by default. When enabled, it is an optional security feature for Mangos Database Studio. It associates a device with future back-end authorization so an administrator can allow, restrict, block, or ban a device when security rules are violated.

Device Identity supplements account authentication. It is not a password, and a device identifier alone does not prove who performed an action.

## Why it is used

The feature is intended to help protect the application and its future back-end services from:

- Unauthorized account or application sharing
- Repeated attempts to bypass access controls
- Malicious database activity
- Abuse of application services
- Devices previously blocked for security or policy violations

## Information collected

When enabled, Mangos Database Studio may read and display:

- Firmware-provided hardware identifier
- Identifier source
- Device manufacturer and model
- Operating platform
- Collection status and time

The feature does not read personal files, browsing history, database records, passwords, or unrelated hardware data.

## Platform behavior

### Windows

Mangos Database Studio reads the SMBIOS hardware UUID through Windows Management Instrumentation. Administrator elevation is normally not required.

### Linux

Mangos Database Studio uses a restricted Polkit helper. The helper requests administrator approval, reads only approved DMI fields, returns the result, and exits immediately. The main application continues under the signed-in user and must not be run as root.

## Current and future use

The current implementation displays Device Identity locally and does not send it to a remote service.

When back-end authentication is introduced, the application may send the identifier through an encrypted connection so the service can determine whether the device is:

- Allowed
- Temporarily restricted
- Blocked
- Banned

Any server-side use should record the decision, reason, and applicable time limit without exposing the identifier unnecessarily in logs or administrative pages.

## Enabling or disabling Device Identity

Device Identity is disabled by default.

Enable it when starting Mangos Database Studio:

```bash
./MangosDatabaseStudio --device-identity
```

On Windows:

```text
MangosDatabaseStudio.exe --device-identity
```

When running from source, the same option can be passed to `scripts/run-linux.sh` or `scripts\run-windows.bat`.

The environment variable `MDS_DEVICE_IDENTITY_ENABLED=1` can also enable it. Use `--no-device-identity` to explicitly disable it.

When disabled:

- The application does not request or read the hardware identifier
- Linux does not display the Polkit authorization prompt
- The profile panel reports **Disabled**
- No Device Identity value is stored or transmitted
- Local features that do not require device authorization remain available

Future back-end features that require device authorization may be unavailable while Device Identity is disabled.

## Limitations

Hardware replacement, firmware updates, motherboard changes, virtual-machine cloning, and manufacturer restrictions can change, duplicate, or remove the identifier. Device blocks should therefore be treated as an additional security control rather than the only basis for an enforcement decision.

## Security expectations

- Use TLS for future remote communication
- Do not use the raw identifier as a password or authentication secret
- Restrict server access to Device Identity records
- Avoid writing full identifiers to ordinary logs
- Retain block or ban records only as long as required for security and administration
- Provide a method to review incorrect device matches or enforcement decisions
