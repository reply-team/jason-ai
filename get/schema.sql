-- The one table: a counter per UTC day, path, source and client class. Nothing else of a request is stored.
CREATE TABLE IF NOT EXISTS counts (
  day TEXT NOT NULL,
  path TEXT NOT NULL,
  ref TEXT NOT NULL,
  client TEXT NOT NULL,
  n INTEGER NOT NULL,
  PRIMARY KEY (day, path, ref, client)
);
