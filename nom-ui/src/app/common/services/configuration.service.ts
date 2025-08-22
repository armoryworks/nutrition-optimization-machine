import { Injectable } from '@angular/core';

@Injectable({
    providedIn: 'root'
})
export class ConfigurationService {

    // Standard measurement units (SI and common units)
    readonly MASS_UNITS = ['kg', 'g', 'mg', 'µg', 'mcg'];
    readonly VOLUME_UNITS = ['L', 'ml', 'cl', 'fl oz', 'cups', 'tablespoons', 'teaspoons'];
    readonly LENGTH_UNITS = ['m', 'cm', 'mm', 'km', 'in', 'ft', 'yd', 'mi'];
    readonly TIME_UNITS = ['s', 'min', 'h', 'days', 'weeks', 'months', 'years'];

    // File size units
    readonly FILE_SIZE_UNITS = ['Bytes', 'KB', 'MB', 'GB', 'TB'];

    // Standard shopping units
    readonly SHOPPING_UNITS = [
        'pieces', 'pounds', 'ounces', 'grams', 'kilograms',
        'cups', 'tablespoons', 'teaspoons', 'liters',
        'milliliters', 'bottles', 'cans', 'boxes', 'bags'
    ];

    // FDA Daily Value standards (in base units)
    readonly FDA_DAILY_VALUES: Record<string, number> = {
        'Total Fat': 78, // g
        'Saturated Fat': 20, // g
        'Trans Fat': 2, // g
        'Cholesterol': 300, // mg
        'Sodium': 2300, // mg
        'Total Carbohydrate': 275, // g
        'Dietary Fiber': 28, // g
        'Total Sugars': 50, // g
        'Protein': 50, // g
        'Vitamin D': 20, // mcg
        'Calcium': 1300, // mg
        'Iron': 18, // mg
        'Potassium': 4700, // mg
    };

    // Nutrient formatting rules
    readonly BOLD_NUTRIENTS = [
        'Total Fat', 'Cholesterol', 'Sodium', 'Total Carbohydrate', 'Protein'
    ];

    readonly INDENTED_NUTRIENTS = [
        'Saturated Fat', 'Trans Fat', 'Dietary Fiber', 'Total Sugars', 'Includes Added Sugars'
    ];

    // Color palettes for consistent UI
    readonly PRIORITY_COLORS = {
        high: '#f44336',    // Red
        medium: '#ff9800',  // Orange
        low: '#4caf50'      // Green
    };

    readonly CATEGORY_COLORS = [
        '#2196f3', '#9c27b0', '#ff5722', '#795548', '#607d8b',
        '#e91e63', '#00bcd4', '#8bc34a', '#ffc107', '#3f51b5',
        '#009688', '#ff9800', '#795548', '#607d8b', '#e91e63'
    ];

    // File upload restrictions
    readonly ALLOWED_FILE_TYPES = {
        image: ['.jpg', '.jpeg', '.png', '.gif', '.bmp', '.webp', '.svg'],
        document: ['.pdf', '.txt', '.md', '.doc', '.docx'],
        archive: ['.zip', '.rar', '.7z', '.tar', '.gz'],
        recipe: ['.json', '.xml', '.yaml', '.yml']
    };

    readonly MAX_FILE_SIZE = 10 * 1024 * 1024; // 10MB

    constructor() { }

    /**
     * Get mass units
     */
    getMassUnits(): string[] {
        return [...this.MASS_UNITS];
    }

    /**
     * Get volume units
     */
    getVolumeUnits(): string[] {
        return [...this.VOLUME_UNITS];
    }

    /**
     * Get length units
     */
    getLengthUnits(): string[] {
        return [...this.LENGTH_UNITS];
    }

    /**
     * Get time units
     */
    getTimeUnits(): string[] {
        return [...this.TIME_UNITS];
    }

    /**
     * Get file size units
     */
    getFileSizeUnits(): string[] {
        return [...this.FILE_SIZE_UNITS];
    }

    /**
     * Get shopping units
     */
    getShoppingUnits(): string[] {
        return [...this.SHOPPING_UNITS];
    }

    /**
     * Get FDA daily value for a nutrient
     */
    getFDADailyValue(nutrientName: string): number | null {
        return this.FDA_DAILY_VALUES[nutrientName] || null;
    }

    /**
     * Check if a nutrient should be bold
     */
    isBoldNutrient(nutrientName: string): boolean {
        return this.BOLD_NUTRIENTS.includes(nutrientName);
    }

    /**
     * Check if a nutrient should be indented
     */
    isIndentedNutrient(nutrientName: string): boolean {
        return this.INDENTED_NUTRIENTS.includes(nutrientName);
    }

    /**
     * Get priority color
     */
    getPriorityColor(priority: string): string {
        const normalizedPriority = priority.toLowerCase();
        return this.PRIORITY_COLORS[normalizedPriority as keyof typeof this.PRIORITY_COLORS] || '#9e9e9e';
    }

    /**
     * Get category color by ID
     */
    getCategoryColor(categoryId: number): string {
        const index = Math.abs(this.hashCode(categoryId.toString())) % this.CATEGORY_COLORS.length;
        return this.CATEGORY_COLORS[index];
    }

    /**
     * Get allowed file types for a category
     */
    getAllowedFileTypes(category: string): string[] {
        return this.ALLOWED_FILE_TYPES[category as keyof typeof this.ALLOWED_FILE_TYPES] || [];
    }

    /**
     * Check if file type is allowed
     */
    isFileTypeAllowed(fileName: string, category: string): boolean {
        const allowedTypes = this.getAllowedFileTypes(category);
        const fileExtension = fileName.toLowerCase().substring(fileName.lastIndexOf('.'));
        return allowedTypes.includes(fileExtension);
    }

    /**
     * Check if file size is within limits
     */
    isFileSizeAllowed(fileSize: number): boolean {
        return fileSize <= this.MAX_FILE_SIZE;
    }

    /**
     * Generate hash code for consistent color distribution
     */
    private hashCode(str: string): number {
        let hash = 0;
        for (let i = 0; i < str.length; i++) {
            const char = str.charCodeAt(i);
            hash = ((hash << 5) - hash) + char;
            hash = hash & hash; // Convert to 32-bit integer
        }
        return hash;
    }
}
