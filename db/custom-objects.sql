-- Database objects NOT managed by the EF Core model.
-- Applied on top of the EF-generated DDL when re-deriving db/schema.sql.

CREATE OR REPLACE VIEW reference."ReferenceGroupView" AS
 SELECT ref."Id" AS "ReferenceId",
    ref."Name" AS "ReferenceName",
    ref."Description" AS "ReferenceDescription",
    grp."Id" AS "GroupId",
    grp."Name" AS "GroupName",
    grp."Description" AS "GroupDescription"
   FROM reference."Reference" ref
     JOIN reference."ReferenceIndex" idx ON ref."Id" = idx."ReferenceId"
     JOIN reference."Group" grp ON grp."Id" = idx."GroupId";
