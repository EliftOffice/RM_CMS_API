-- Add Worship Module columns to songs table
ALTER TABLE songs
ADD COLUMN service_date DATE NULL,
ADD COLUMN service_type TINYINT NULL,
ADD COLUMN service_category VARCHAR(50) NULL;