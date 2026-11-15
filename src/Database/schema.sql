-- historical draft schema, never applied: each service owns its own EF migrations. sign-in is delegated to the identity provider,
-- so a real deployment stores no password at all; the column below only ever holds a hash.
create type "TransportMode" as enum ('car', 'bus', 'train', 'pedestrian', 'plane', 'bike');
create type "TripType" as enum ('one_way', 'round_trip');

create table "Users" (
    "Id" bigint primary key,
    "Uuid" uuid not null unique,
    "UserName" text not null unique,
    "PasswordHash" text not null,
    "Address" text
);

create table "Accommodations" (
    "Id" bigint primary key,
    "Uuid" uuid not null unique,
    "Name" text not null,
    "Address" text
);

create table "Attractions" (
    "Id" bigint primary key,
    "Uuid" uuid not null unique,
    "Name" text not null,
    "Address" text
);

create table "Trips" (
    "Id" bigint primary key,
    "Uuid" uuid not null unique,
    "UserId" bigint not null references "Users" ("Id"),
    "Transport" "TransportMode" not null,
    "Type" "TripType" not null,
    "DeparturePlaceId" bigint not null references "Accommodations" ("Id"),
    "DestinationPlaceId" bigint not null references "Attractions" ("Id")
);

create index "IX_Trips_UserId" on "Trips" ("UserId");
