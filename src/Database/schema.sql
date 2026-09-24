create type "TransportMode" as enum ('car', 'bus', 'train', 'pedestrian', 'plane');
create type "TripType" as enum ('one_way', 'round_trip');

create table "Users" (
    "Id" bigint generated always as identity primary key,
    "Uuid" uuid not null unique,
    "UserName" text not null unique,
    "Password" text not null,
    "Address" text
);

create table "Accommodations" (
    "Id" bigint generated always as identity primary key,
    "Uuid" uuid not null unique,
    "Name" text not null,
    "Address" text
);

create table "Attractions" (
    "Id" bigint generated always as identity primary key,
    "Uuid" uuid not null unique,
    "Name" text not null,
    "Address" text
);

create table "Trips" (
    "Id" bigint generated always as identity primary key,
    "Uuid" uuid not null unique,
    "UserId" bigint not null references "Users" ("Id"),
    "Transport" "TransportMode" not null,
    "Type" "TripType" not null,
    "DeparturePlaceId" bigint not null references "Accommodations" ("Id"),
    "DestinationPlaceId" bigint not null references "Attractions" ("Id")
);
