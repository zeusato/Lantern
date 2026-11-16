CREATE TABLE `relay_routes` (`id` text PRIMARY KEY NOT NULL, `host_hash` text NOT NULL, `client_hash` text NOT NULL, `session` text NOT NULL DEFAULT '', `expires` integer NOT NULL);
--> statement-breakpoint
CREATE TABLE `relay_packets` (`route` text NOT NULL, `session` text NOT NULL, `side` text NOT NULL, `seq` integer NOT NULL, `body` text NOT NULL, `created` integer NOT NULL, PRIMARY KEY(`route`,`session`,`side`,`seq`));
--> statement-breakpoint
CREATE INDEX `relay_expiry` ON `relay_routes` (`expires`);
--> statement-breakpoint
CREATE INDEX `relay_packet_expiry` ON `relay_packets` (`created`);
