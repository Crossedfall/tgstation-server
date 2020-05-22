# Service Components

Component code is where the magic and tears of TGS are made. There are six main TGS components that map to namespaces in this directory.

- [The git repository](./Repository)
- [The BYOND manager](./Byond)
- [The Compiler](./Deployment)
- [The Watchdog](./Watchdog)
- [The configuration system](./StaticFiles)

There exist two more namespaces in here that don't fit in these 6.

- [Interop](./Interop) deals with the bulk of DMAPI communication (Though it's not all contained here).
- [Session](./Session) contains the classes used for actually executing DreamDaemon among other things.

Each of these is tied under the roof of an [IInstance](./IInstance.cs) ([implementation](./Instance.cs)).

While the database represents stored instance data, in component code an instance is online, or doesn't exist.

`IInstance`s are created via the [IInstanceFactory](./IInstanceFactory.cs) ([implementation](./InstanceFactory.cs)) and are generally controlled via the [IInstanceManager](./IInstanceManager.cs) ([implementation](./InstanceManager.cs)).