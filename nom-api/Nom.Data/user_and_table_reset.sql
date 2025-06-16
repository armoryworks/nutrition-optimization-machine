-- This script performs a full cleanup by dropping and recreating specific application schemas
-- and removing the EF Core migrations history table.
-- It should be run by a PostgreSQL superuser (e.g., 'postgres') in a development environment.

DO
$$
DECLARE
    -- Reusable variables
    app_db_user TEXT := 'your_db_user';               -- !!! REPLACE WITH YOUR ACTUAL EF CORE USERNAME !!!
    app_db_password TEXT := 'your_db_password';  -- !!! REPLACE WITH YOUR ACTUAL PASSWORD (if creating user) !!!
    app_db_name TEXT := 'your_database_name';    -- !!! REPLACE WITH YOUR DATABASE NAME !!!
    -- List of application schemas to drop and recreate
    schemas_to_manage TEXT[] := ARRAY'public', ['auth', 'reference', 'plan', 'recipe', 'nutrient', 'shopping'];

    schema_name TEXT;
BEGIN
    RAISE NOTICE 'Starting full schema and migration history cleanup...';

    -- Optional: 1. Create the application database user if it doesn't exist
    -- This block can be uncommented and used for initial setup, otherwise assume user exists.
    -- IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = app_db_user) THEN
    --     EXECUTE format('CREATE ROLE %I WITH LOGIN PASSWORD %L;', app_db_user, app_db_password);
    -- END IF;

    -- Optional: 2. Grant CONNECT and CREATE on the database to the user
    -- These are typically needed for the user to manage schemas and connect.
    -- EXECUTE format('GRANT CONNECT ON DATABASE %I TO %I;', app_db_name, app_db_user);
    -- EXECUTE format('GRANT CREATE ON DATABASE %I TO %I;', app_db_name, app_db_user);


    RAISE NOTICE 'Dropping schemas with CASCADE...';
    -- 3. Drop all specified application schemas (CASCADE to remove all contained objects)
    FOREACH schema_name IN ARRAY schemas_to_manage
    LOOP
        RAISE NOTICE 'Dropping schema: %', schema_name;
        EXECUTE format('DROP SCHEMA IF EXISTS %I CASCADE;', schema_name);
    END LOOP;

    RAISE NOTICE 'Dropping __EFMigrationsHistory table...';
    -- 4. Drop the EF Core migrations history table from the 'public' schema
    --    This table tracks applied migrations and needs to be clean for new migrations.
    EXECUTE 'DROP TABLE IF EXISTS public."__EFMigrationsHistory" CASCADE;';


    RAISE NOTICE 'Recreating schemas and setting permissions...';
    -- 5. Recreate all specified application schemas and re-assign ownership/permissions
    FOREACH schema_name IN ARRAY schemas_to_manage
    LOOP
        RAISE NOTICE 'Creating schema: %', schema_name;
        EXECUTE format('CREATE SCHEMA %I;', schema_name);

        -- Grant ownership of the schema to the application user. This is CRUCIAL for future `DROP SCHEMA` permissions.
        EXECUTE format('ALTER SCHEMA %I OWNER TO %I;', schema_name, app_db_user);

        -- Grant all privileges on the schema itself (for the user)
        EXECUTE format('GRANT ALL PRIVILEGES ON SCHEMA %I TO %I;', schema_name, app_db_user);

        -- Set DEFAULT PRIVILEGES for future objects created by the application user in this schema
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL PRIVILEGES ON TABLES TO %I;',
                       app_db_user, schema_name, app_db_user);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL PRIVILEGES ON SEQUENCES TO %I;',
                       app_db_user, schema_name, app_db_user);

        -- Optional: Add similar DEFAULT PRIVILEGES for functions, types, etc., if your application creates them
        -- EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL PRIVILEGES ON FUNCTIONS TO %I;', app_db_user, schema_name, app_db_user);
        -- EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL PRIVILEGES ON TYPES TO %I;', app_db_user, schema_name, app_db_user);

    END LOOP;

    RAISE NOTICE 'Schema cleanup and recreation complete.';

END
$$;