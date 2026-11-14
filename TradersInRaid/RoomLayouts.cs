namespace TradersInRaid
{
    /// <summary>
    /// What stands where in each trader's room. One piece per line:
    ///   NAME X Z YAW [HEIGHT] [N|E|S|W] [n] [table] [s=SCALE]
    /// NAME is an object of the game's hideout interior. X and Z are metres from the middle of the
    /// room's floor (the room is 6 wide and 7 long; the door is in the south wall, the trader stands
    /// to the north). A wall letter pushes the piece flat against that wall. "n" lets the player walk
    /// through it, "table" makes it the table deliveries are laid on, "door" makes it the way out.
    /// A heading names the trader and the materials of the walls, floor and ceiling; [*] applies to all.
    /// </summary>
    public static class RoomLayouts
    {
        public const string Text = @"
[*] ceiling=oil_paint_white
Old_Table_2 0 -0.4 0 E table
table_bank_6 0 1.2 90
Lum_lamp_hideout 0 0 0 2.8 n
Bunker_Door_B_01_R_hideout_220-110 0 0 0 S n door

[Base] wall=City_Concrete_New_Rough_light_03 floor=arena_equator_concrete_01

[Prapor] wall=City_Concrete_New_Rough_light_03 floor=arena_equator_concrete_01
Weapon_stand_Lv1 0 0 0 N
wood_box_closed -2.3 2.6 0 W
military_AmmoBox122mm_Closed -2.4 1.2 0 W
cupboard_metal -2.4 -0.2 0 W
weapon_box_single -2.3 -1.6 0 W
barrel_metal_1 -2.4 -2.9 0
scontainer_gunrack 2.3 2.7 0 E
scontainer_ammo_grenade 2.4 1.5 0 E
item_container_weaponcase 0.3 1.2 90 0.87 n

[Therapist] wall=City_Concrete_Rough_white floor=City_plastic_dark_grey_floor
Dropper -1.3 2.7 0
Bathroom_Sink 1.9 0 0 N
cupboard -2.4 1.6 180 W
refrigerator_minsk15m 2.5 2.4 0 E
armchair -2.4 -0.4 90 W
stool -1.5 -1.4 0
scontainer_medbag_smu06 -2.4 -2.4 0 W
scontainer_medcase 0.5 1.2 0 0.87 n
lab_microscope -0.5 1.2 0 0.87 n

[Skier] wall=brick_modern_red floor=City_Concrete_Floor_A1_light
carpet -1.4 -0.8 0 n
sofa -2.4 -0.8 0 W
Coffee_Table_04 -1.3 -0.8 0
Night_stand -2.2 3.0 0 N
TV -2.2 3.0 -90 0.92 N n
Beer_Barrel 2.4 2.9 0
scontainer_gunsafe_valberg 1.6 0 180 N
Plastic_suitcase_110_P -2.3 1.6 0 W
ArmChair_1 -2.2 -2.7 0
Wine_Bottle_02 0.6 1.2 0 0.87 n

[Peacekeeper] wall=Concrete_bricks_seamless floor=City_Concrete_slab_rough
military_podnos_box -2.4 2.7 0 W
Plastic_suitcase_140_P -2.3 1.2 0 W
cupboard_metal_rezerve 2.2 0 0 N
reserve_woodBox_small -2.3 -2.6 0 W
weapon_box_single -2.3 -0.6 0 W
folding_chair -1.4 -1.8 0
M3RT_Radiostation 0.5 1.2 0 0.87 n
laptop_open -0.5 1.2 0 0.87 n

[Mechanic] wall=City_Concrete_slab_rough floor=arena_equator_concrete_01
rack_shelf -2.4 2.7 0 W
Metal_Table2 -2.4 0.8 90 W
Vise -2.5 0.8 0 0.9 n
scontainer_toolbox -2.4 -0.8 0 W
metallBox_medium01 -2.4 -2.4 0 W
PC_block 2.0 3.0 0 N
PC_block 2.5 3.0 0 N
solar_generator_LFP_80 -1.6 3.0 0 N
PC_monitor -0.4 1.2 0 0.87 n
PC_keyboard -0.4 0.95 0 0.87 n
laptop_open 0.6 1.2 0 0.87 n

[Ragman] wall=City_wood_veneer_cardinal floor=City_Carpet_3
shelves_store_small01 -2.4 2.4 0 W
dressing_room_bench_01 -2.5 0.2 0 W
cupboard_02_close 2.0 0 0 N
jacket01 -1.8 0 0 1.0 N n
jacket03 -1.2 0 0 1.0 N n
Cloth_Jacket_4_v3 -2.4 0 0 1.0 N n
bag_sport -2.3 -2.6 0 n
shoe_hall_boots_1 -2.4 -1.2 0 n
Clothes_dryer_Closed -2.5 -1.9 0 W
clothes_stack_01 0.5 1.2 0 0.87 n

[Jaeger] wall=Reserve_Wood_Wall_Light floor=Saw_wood_planks
burzhuyka -2.3 0 0 N
Firewood_pile_3 -2.4 1.0 0 W
Deer_Horns 0 0 0 1.9 N n
armchair_old_01 -2.2 -1.2 0
Night_stand -2.4 -2.6 0 W
carpet -0.3 -1.2 0 n
wood_box 2.2 0 0 N
shovel -2.9 -0.1 0 W n
Coal_Bag_1 -1.5 2.9 0 n
kanistra_metall 0.6 1.2 0 0.87 n

[Fence] wall=Concrete_bricks_seamless floor=City_Concrete_Floor_A1_light
supermarket_trolley_P -1.9 -1.6 0
cardboard_box1 -2.5 2.9 0 W
cardboard_box1 -2.5 2.2 20 W
cardboard_box2 -2.5 2.8 0 0.62 n
jcb_Tire 2.3 0 0 N
metallBox_small01 -2.4 0.8 0 W
TV -2.4 0.8 90 0.5 W n
pallet_barrel_plastic_P -2.2 -2.6 0
Washer_Update -1.5 0 0 N
Typewriter_01 0.5 1.2 180 0.87 n

[Ref] wall=Hideout_plastic_vertical_black floor=City_plastic_dark_grey_floor
gym_sportMat1 -1.8 -1.6 0 n
Punching_bag_02 -2.0 -0.2 0
training_bench_set_1_update -2.4 1.6 0 W
barbell_update -2.2 2.9 0 n
dumbbells_1_update -2.6 -2.9 0 n
dumbbells_2_update -2.1 -2.9 0 n
TV_LCD_only_TV 2.0 0 180 1.5 N n
Shooting_range_popper -1.6 0 0 N
01_target_monitor 0.5 1.2 0 0.87 n
";
    }
}
