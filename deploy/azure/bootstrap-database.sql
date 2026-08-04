\set ON_ERROR_STOP on

SELECT format('CREATE ROLE ww_migrator LOGIN PASSWORD %L', :'migrator_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'ww_migrator') \gexec
SELECT format('CREATE ROLE ww_app LOGIN PASSWORD %L', :'app_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'ww_app') \gexec

ALTER ROLE ww_migrator PASSWORD :'migrator_password';
ALTER ROLE ww_app PASSWORD :'app_password';
ALTER DATABASE :"database_name" OWNER TO ww_migrator;
ALTER SCHEMA public OWNER TO ww_migrator;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM ww_app;
GRANT CONNECT ON DATABASE :"database_name" TO ww_migrator, ww_app;
GRANT USAGE ON SCHEMA public TO ww_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO ww_app;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO ww_app;
ALTER DEFAULT PRIVILEGES FOR ROLE ww_migrator IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO ww_app;
ALTER DEFAULT PRIVILEGES FOR ROLE ww_migrator IN SCHEMA public
  GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO ww_app;
