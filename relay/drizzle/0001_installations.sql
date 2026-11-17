CREATE TABLE `relay_installations` (`key_hash` text PRIMARY KEY NOT NULL, `created` integer NOT NULL, `last_seen` integer NOT NULL, `source_hash` text NOT NULL, `route_day` integer NOT NULL DEFAULT 0, `route_count` integer NOT NULL DEFAULT 0);
--> statement-breakpoint
CREATE INDEX `relay_installation_created` ON `relay_installations` (`created`);
--> statement-breakpoint
CREATE INDEX `relay_installation_source` ON `relay_installations` (`source_hash`, `created`);
--> statement-breakpoint
ALTER TABLE `relay_routes` ADD `installation_hash` text;
--> statement-breakpoint
CREATE INDEX `relay_route_installation` ON `relay_routes` (`installation_hash`, `expires`);
