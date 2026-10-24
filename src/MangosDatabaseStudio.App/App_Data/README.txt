Runtime-only application data is created here.

Do not commit or redistribute the contents of this directory. Back up the
following items together for each installed instance:

- database/mangos-studio.db
- keys/application-database.key
- keys/*.xml (ASP.NET Core Data Protection keys)
- avatars/ (when uploaded avatars are used)

Temporary SQL imports and exports are stored under imports/ and exports/ and
are removed after their operation completes.

On an upgrade from the JSON stores, preserve users.json, connections.json,
role-permissions.json, and the matching Data Protection XML keys until the
first SQLCipher migration completes successfully.
