[![license](https://img.shields.io/github/license/mashape/apistatus.svg)]()

### Features

- Supports clusters of pools each running individual currencies
- Ultra-low-latency, multi-threaded Stratum implementation using asynchronous I/O
- Adaptive share difficulty ("vardiff")
- PoW validation (hashing) using native code for maximum performance
- Session management for purging DDoS/flood initiated zombie workers
- Payment processing
- Banning System
- Live Stats [API](https://github.com/cyberpoolorg/cybercore/wiki/API) on Port 4000
- WebSocket streaming of notable events like Blocks found, Blocks unlocked, Payments and more
- PoW (proof-of-work) & PoS (proof-of-stake) support
- Detailed per-pool logging to console & filesystem

### Supported Currencies

Refer to [this file](https://github.com/cyberpoolorg/cybercore/blob/master/src/Cybercore/coins.json) for a complete list.

#### Monero

- Monero's Wallet Daemon (monero-wallet-rpc) relies on HTTP digest authentication for authentication which is currently not supported by Cybercore. Therefore monero-wallet-rpc must be run with the --disable-rpc-login option. It is advisable to mitigate the resulting security risk by putting monero-wallet-rpc behind a reverse proxy like nginx with basic-authentication.
- Cybercore utilizes RandomX's light-mode by default which consumes only **256 MB of memory per RandomX-VM**. A mondern (2021) era CPU will be able to handle ~ 50 shares per second in this mode.
- If you are running into throughput problems on your pool you can either increase the number of RandomX virtual machines in light-mode by adding `"randomXVmCount": x` to your pool configuration where x is at maximum equal to the machine's number of processor cores. Alternatively you can activate fast-mode by adding `"randomXFlagsAdd": "RANDOMX_FLAG_FULL_MEM"` to the pool configuration. Fast mode increases performance by 10x but requires roughly **3 GB of RAM per RandomX-VM**.


#### ZCash

- Pools needs to be configured with both a t-addr and z-addr (new configuration property "z-address" of the pool configuration element)
- First configured zcashd daemon needs to control both the t-addr and the z-addr (have the private key)
- To increase the share processing throughput it is advisable to increase the maximum number of concurrent equihash solvers through the new configuration property "equihashMaxThreads" of the cluster configuration element. Increasing this value by one increases the peak memory consumption of the pool cluster by 1 GB.
- Miners may use both t-addresses and z-addresses when connecting to the pool


#### Ethereum

- Cybercore implements the [Ethereum stratum mining protocol](https://github.com/nicehash/Specifications/blob/master/EthereumStratum_NiceHash_v1.0.0.txt) authored by NiceHash. This protocol is implemented by all major Ethereum miners.
- Claymore Miner must be configured to communicate using this protocol by supplying the `-esm 3` command line option
- Genoil's `ethminer` must be configured to communicate using this protocol by supplying the `-SP 2` command line option


#### Vertcoin

- Be sure to copy the file verthash.dat from your vertcoin blockchain folder to your Miningcore server
- In your Miningcore config file add this property to your vertcoin pool configuration: "vertHashDataFile": "/path/to/verthash.dat",


### Donations

You can send donations directly to the following accounts:

* BTC - `1H8Ze41raYGXYAiLAEiN12vmGH34A7cuua`
* LTC - `LSE19SHK3DMxFVyk35rhTFaw7vr1f8zLkT`
* ZEC - `t1NTX2qJAhQrEdTRNaqVckznNMaqUSwPLvp`
* ETH - `0x52FdE416C1D51525aEA390E39CfD5016dAFC01F7`
* ETC - `0x6F2B787312Df5B08a6b7073Bdb8fF04442B6A11f`

### Building from Source

#### Building on Ubuntu 20.04

```console
$ wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
$ sudo dpkg -i packages-microsoft-prod.deb
$ sudo apt-get update
$ sudo apt-get install -y apt-transport-https build-essential software-properties-common curl unzip rar htop git
$ sudo apt-get install -y libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq3-dev libzmq5 screen cmake
$ sudo apt-get install -y postgresql postgresql-contrib dotnet-sdk-6.0
$ git clone https://github.com/lurchinms/cybercore_NET6.git
$ cd cybercore_NET6/src/Cybercore
$ dotnet publish -c Release --framework net6.0 -o ../../build
```

#### After successful build

Create a configuration file `config.json` as described [here](https://github.com/cyberpoolorg/cybercore/wiki/Configuration)

```console
$ cd ../../build
$ Cybercore.dll -c config.json
```

### Basic PostgreSQL Database setup

Create the database:

```console
$ createuser cybercore
$ createdb cybercore
$ psql (enter the password for postgres)
```

Inside `psql` execute:

```sql
alter user cybercore with encrypted password 'some-secure-password';
grant all privileges on database cybercore to cybercore;
```

Import the database schema:

```console
$ wget https://raw.githubusercontent.com/lurchinms/cybercore_NET6/master/src/Cybercore/Persistence/Postgres/Scripts/createdb.sql
$ psql -d cybercore -U cybercore -f createdb.sql
```

### Advanced PostgreSQL Database setup

If you are planning to run a Multipool-Cluster, the simple setup might not perform well enough under high load. In this case you are strongly advised to use PostgreSQL 11 or higher. After performing the steps outlined in the basic setup above, perform these additional steps:

**WARNING**: The following step will delete all recorded shares. Do **NOT** do this on a production pool unless you backup your `shares` table using `pg_backup` first!

```console
$ wget https://raw.githubusercontent.com/lurchinms/cybercore_NET6/master/src/Cybercore/Persistence/Postgres/Scripts/createdb_postgresql_11_appendix.sql
$ psql -d cybercore -U cybercore -f createdb_postgresql_11_appendix.sql
```

After executing the command, your `shares` table is now a [list-partitioned table](https://www.postgresql.org/docs/11/ddl-partitioning.html) which dramatically improves query performance, since almost all database operations Cybercore performs are scoped to a certain pool.

The following step needs to performed **once for every new pool** you add to your cluster. Be sure to **replace all occurences** of `mypool` in the statement below with the id of your pool from your Cybercore configuration file:

```sql
CREATE TABLE shares_mypool PARTITION OF shares FOR VALUES IN ('mypool');
```

Once you have done this for all of your existing pools you should now restore your shares from backup.

### [Configuration](https://github.com/cyberpoolorg/cybercore/wiki/Configuration)

### [API](https://github.com/cyberpoolorg/cybercore/wiki/API)

## Running a production pool

A public production pool requires a web-frontend for your users to check their statistics. Cybercore does not include such frontend at the moment.
