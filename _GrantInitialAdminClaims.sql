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
-- 1. Find the Id of the first user in the "auth"."AspNetUsers" table.
-- 2. Replace the placeholder 'YOUR_FIRST_USER_ID_HERE' with the actual user Id.
-- 3. Run this script against your application's database.
-- ==========================================================================================

DO $$
DECLARE
    -- !!! IMPORTANT !!!
    -- !!! Replace this placeholder with the actual Id of the first registered user. !!!
    first_user_id TEXT := 'YOUR_FIRST_USER_ID_HERE';

    -- Define the claim types to be granted.
    manage_roles_claim TEXT := 'CanManageUserRoles';
    manage_curation_claim TEXT := 'CanManageCuration';

BEGIN
    -- Check if the placeholder has been replaced
    IF first_user_id = 'YOUR_FIRST_USER_ID_HERE' THEN
        RAISE EXCEPTION 'Placeholder user ID has not been replaced. Please edit this script before running.';
    END IF;

    -- Check if the user exists before attempting to insert claims
    IF NOT EXISTS (SELECT 1 FROM "auth"."AspNetUsers" WHERE "Id" = first_user_id) THEN
        RAISE EXCEPTION 'User with ID % not found in "auth"."AspNetUsers". Please verify the user ID.', first_user_id;
    END IF;

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