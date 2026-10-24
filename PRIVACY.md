# Privacy

This document explains how Mangos Database Studio handles application data, including the optional Device Identity feature.

## Data handled by the application

Depending on the enabled features, the application may process:

- Usernames, passwords, roles, permissions, avatars, and preferences
- Saved MySQL or MariaDB connection profiles
- SQL imports and application activity required to perform requested operations
- Optional Device Identity information

The application should collect only the information needed to provide its configured features and security controls.

## Local application storage

Mangos Database Studio is a desktop application. Its embedded local transport runs inside the main desktop process and stores runtime data under `App_Data`. Account, role, preference, update-source, and connection-profile data is stored in the encrypted application database. Encryption keys and ASP.NET Core Data Protection keys are stored separately and must be protected and backed up with the database.

Saved database credentials should remain encrypted and accessible only to authorized application functions and administrators.

## Device Identity privacy

When enabled, Device Identity may include a firmware-provided hardware identifier, identifier source, manufacturer, model, platform, availability status, and collection time.

It is used as an additional security signal for future client authorization and may support allowing, restricting, blocking, or banning a device after abuse or security violations.

Device Identity does not inspect personal files, browsing activity, messages, database content, or unrelated hardware information.

The current implementation displays the result locally and does not transmit it to a remote service. Before remote authentication is enabled, the application should clearly identify what is sent, where it is stored, and how long it is retained.

## User choice

Device Identity can be disabled completely.

When disabled, the application does not collect, display, store, or transmit a Device Identity value. Linux also does not request Polkit authorization. Features that depend on future back-end device authorization may not be available while the option is disabled.

Users can view the collected Device Identity information from their profile when the feature is enabled.

## Future back-end processing

When a back-end authorization service is introduced, Device Identity may be transmitted over TLS and compared with an authorization record. The service may retain an allowed, restricted, blocked, or banned status together with an administrative reason and relevant timestamps.

A Device Identity match should not be treated as proof of a person's identity or as the sole evidence for an enforcement decision. Hardware identifiers can change, be unavailable, or be duplicated by some virtualized systems.

## Data sharing and advertising

Mangos Database Studio is not designed to sell Device Identity information or use it for advertising. Device Identity should be shared only with a configured authentication service and personnel authorized to administer access or investigate security events.

## Retention and removal

Local application data remains until it is removed through the application, deleted by an administrator, or removed with the application's data directory.

Future server-side Device Identity records should be retained only while needed for authorization, an active restriction, security review, or a limited audit period.

## Security measures

The application uses or is designed to use:

- Encrypted application storage
- Protected database credentials
- Role-based access control
- Restricted hardware access
- Loopback-only embedded application transport protected by a per-launch desktop session
- TLS for future remote communication
- Limited administrative access to security records

No security measure eliminates every risk. Administrators are responsible for protecting application files, encryption keys, database accounts, backups, and any future back-end services.

## Changes

This privacy information should be updated before enabling new remote collection, analytics, authentication, or data-sharing behavior. Material changes should be shown to users before the updated behavior is enabled.
