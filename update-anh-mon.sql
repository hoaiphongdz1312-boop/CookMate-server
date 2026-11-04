-- Gắn ảnh cho 2 món chưa có ảnh. Chạy trên database db_ad0915_cookmate (MyASP).
UPDATE Recipes SET ImageUrl = N'/images/recipes/trung-chien-hanh.jpg'
WHERE Name = N'Trứng chiên hành' AND AuthorId IS NULL;

UPDATE Recipes SET ImageUrl = N'/images/recipes/canh-cai-thit-bam.jpg'
WHERE Name = N'Canh cải thịt bằm' AND AuthorId IS NULL;
