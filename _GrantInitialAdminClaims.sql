-- ==========================================================================================
-- Grant Initial Administrative Claims
-- ==========================================================================================
-- Description:
-- This script grants the essential administrative claims ("CanManageUserRoles" and
-- "CanManageCuration") to the first registered user in the system. It should be run
-- once after the initial database migration and after the first user has created
-- their account.
--
-- Instructions:
-- 1. This script automatically finds the first user in the "auth"."AspNetUsers" table.
-- 2. Run this script against your application's database.
-- ==========================================================================================

DO $$
DECLARE
    -- Automatically get the first user ID from the AspNetUsers table
    first_user_id TEXT;
    
    -- Define the claim types to be granted.
    manage_roles_claim TEXT := 'CanManageUserRoles';
    manage_curation_claim TEXT := 'CanManageCuration';

BEGIN
    -- Get the first user ID from the AspNetUsers table
    SELECT "Id" INTO first_user_id
    FROM "auth"."AspNetUsers"
    ORDER BY "Id"
    LIMIT 1;
    
    -- Check if any users exist
    IF first_user_id IS NULL THEN
        RAISE EXCEPTION 'No users found in "auth"."AspNetUsers". Please create a user account first.';
    END IF;
    
    RAISE NOTICE 'Found first user with ID: %', first_user_id;

    -- Grant the 'CanManageUserRoles' claim if it doesn't already exist for the user.
    -- This claim allows the user to grant or revoke administrative roles for other users.
    IF NOT EXISTS (SELECT 1 FROM "auth"."AspNetUserClaims" WHERE "UserId" = first_user_id AND "ClaimType" = manage_roles_claim) THEN
        INSERT INTO "auth"."AspNetUserClaims" ("UserId", "ClaimType", "ClaimValue")
        VALUES (first_user_id, manage_roles_claim, 'true');
        RAISE NOTICE 'Granted % claim to user %', manage_roles_claim, first_user_id;
    ELSE
        RAISE NOTICE '% claim already exists for user %', manage_roles_claim, first_user_id;
    END IF;

    -- Grant the 'CanManageCuration' claim if it doesn't already exist for the user.
    -- This claim allows the user to manage the recipe and ingredient curation queue.
    IF NOT EXISTS (SELECT 1 FROM "auth"."AspNetUserClaims" WHERE "UserId" = first_user_id AND "ClaimType" = manage_curation_claim) THEN
        INSERT INTO "auth"."AspNetUserClaims" ("UserId", "ClaimType", "ClaimValue")
        VALUES (first_user_id, manage_curation_claim, 'true');
        RAISE NOTICE 'Granted % claim to user %', manage_curation_claim, first_user_id;
    ELSE
        RAISE NOTICE '% claim already exists for user %', manage_curation_claim, first_user_id;
    END IF;

    RAISE NOTICE 'Script finished. Claims checked for user %.', first_user_id;

END $$;