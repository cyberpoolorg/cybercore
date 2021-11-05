SET ROLE cybercore;

DROP TABLE minerstats;

CREATE TABLE minerstats
(
	id BIGSERIAL NOT NULL PRIMARY KEY,
	poolid TEXT NOT NULL,
	miner TEXT NOT NULL,
	worker TEXT NOT NULL,
	hashrate DOUBLE PRECISION NOT NULL DEFAULT 0,
	sharespersecond DOUBLE PRECISION NOT NULL DEFAULT 0,
	ipaddress TEXT NULL,
	balance decimal(28,12) NOT NULL DEFAULT 0,
	source TEXT NULL,
	created TIMESTAMP NOT NULL
);

CREATE INDEX IDX_MINERSTATS_POOL_CREATED on minerstats(poolid, created);
CREATE INDEX IDX_MINERSTATS_POOL_MINER_CREATED on minerstats(poolid, miner, created);
CREATE INDEX IDX_MINERSTATS_POOL_MINER_CREATED_HOUR on minerstats(poolid, miner, date_trunc('hour',created));
CREATE INDEX IDX_MINERSTATS_POOL_MINER_CREATED_DAY on minerstats(poolid, miner, date_trunc('day',created));
CREATE INDEX IDX_MINERSTATS_CREATED_POOL_MINER_WORKER_HASHRATE_IPADDRESS_BALANCE_SOURCE on minerstats(created desc,poolid,miner,worker,hashrate,ipaddress,balance,source);