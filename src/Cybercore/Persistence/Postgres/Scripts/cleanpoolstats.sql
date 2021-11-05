SET ROLE cybercore;

DROP TABLE poolstats;

CREATE TABLE poolstats
(
	id BIGSERIAL NOT NULL PRIMARY KEY,
	poolid TEXT NOT NULL,
	connectedminers INT NOT NULL DEFAULT 0,
	connectedworkers INT NOT NULL DEFAULT 0,
	poolhashrate DOUBLE PRECISION NOT NULL DEFAULT 0,
	sharespersecond DOUBLE PRECISION NOT NULL DEFAULT 0,
	roundshares DOUBLE PRECISION NOT NULL DEFAULT 0,
	roundeffort DOUBLE PRECISION NOT NULL DEFAULT 0,
	networkhashrate DOUBLE PRECISION NOT NULL DEFAULT 0,
	networkdifficulty DOUBLE PRECISION NOT NULL DEFAULT 0,
	lastnetworkblocktime TIMESTAMP NULL,
	blockheight BIGINT NOT NULL DEFAULT 0,
	connectedpeers INT NOT NULL DEFAULT 0,
	created TIMESTAMP NOT NULL,
	lastpoolblocktime TIMESTAMP NULL
);

CREATE INDEX IDX_POOLSTATS_POOL_CREATED on poolstats(poolid, created);
CREATE INDEX IDX_POOLSTATS_POOL_CREATED_HOUR on poolstats(poolid, date_trunc('hour',created));